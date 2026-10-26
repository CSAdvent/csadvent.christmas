using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Backups;

public enum BackupSignupAvailability
{
    Open,
    NotOpen,
    SeasonOver,
}

/// <summary>Someone's place on a season's backup list. <see cref="FilledSpot"/> is set once they've filled one.</summary>
public sealed record BackupMembership(int? Position, string? FilledSpot);

/// <summary>Someone's standing with a season's backup list, for My spots (#175): their place, and the asks they can answer.</summary>
/// <param name="Membership">Their place on the list, if they're on it.</param>
public sealed record MyBackups(int Year, BackupSignupAvailability Availability, BackupMembership? Membership, IReadOnlyList<MyBackupOffer> Offers);

/// <summary>A spot they've been asked to fill, whose day isn't over. <paramref name="StandingBy"/> once they've said yes.</summary>
public sealed record MyBackupOffer(int OfferId, int Year, int Day, int Part, int SlotsPerDay, bool StandingBy)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day} · Post {Part}" : $"December {Day}";

    public string Page => $"backups/offers/{OfferId}";
}

public sealed record BackupListEntry(
    int? Position,
    string Name,
    string? GithubLogin,
    string Email,
    string? Notes,
    DateTimeOffset SignedUpAt,
    string Status);

/// <summary>A backup poster asked about one spot, for its entry editor: who, when, and what they said (#173).</summary>
public sealed record SpotBackupOffer(
    string Name,
    string? GithubLogin,
    string Email,
    DateTimeOffset AskedAt,
    DateTimeOffset ExpiresAt,
    BackupOfferStatus Status,
    DateTimeOffset? RespondedAt,
    string? PostTitle,
    string? PostUrl);

/// <summary>
/// A backup asked about a spot who hasn't answered yet, with time left, or one standing by to take it (<paramref name="StandingBy"/>).
/// </summary>
/// <param name="ExpiresAt">For an ask, when the next backup is asked too.</param>
public sealed record ActiveBackupOffer(int SlotId, int SubstituteId, int Day, int Part, string Name, bool StandingBy, DateTimeOffset AskedAt, DateTimeOffset ExpiresAt);

public enum BackupSignupOutcome
{
    SignedUp,

    /// <summary>They're on the list again, after leaving it this season. They had the sign-up email the first time.</summary>
    Rejoined,
    AlreadyOnList,
    NoVerifiedEmail,
    NotOpen,
}

/// <summary>
/// A season's backup list: anyone signed in can join while sign-ups are open (including people with a spot), and it's
/// kept in sign-up order. The organizers can see it at <c>/manage/backups</c>.
/// </summary>
public sealed class BackupListService(
    AdventDbContext db,
    AdventSchedule schedule,
    TimeProvider timeProvider,
    EmailComposer composer,
    IOptions<SiteOptions> site,
    SeasonAccess access)
{
    public async Task<BackupSignupAvailability?> GetAvailabilityAsync(int year, CancellationToken cancellationToken = default)
    {
        var season = access.CanSee(year) ? await db.Seasons.AsNoTracking().SingleOrDefaultAsync(s => s.Year == year, cancellationToken) : null;
        return season is null ? null
            : schedule.IsFinished(year) ? BackupSignupAvailability.SeasonOver
            : season.IsTest || schedule.AreClaimsOpen(season.ClaimsOpenAt) ? BackupSignupAvailability.Open
            : BackupSignupAvailability.NotOpen;
    }

    public async Task<BackupMembership?> GetMembershipAsync(int contributorId, int year, CancellationToken cancellationToken = default)
    {
        var list = await OrderedAsync(year, cancellationToken);
        var mine = list.FirstOrDefault(s => s.ContributorId == contributorId);
        if (mine is null)
        {
            return null;
        }

        return new BackupMembership(PositionOf(list, mine), mine.AssignedSlot is { } slot ? $"December {slot.Day}" : null);
    }

    /// <summary>
    /// Their standing with a season's backup list, while it's running: their place, and every ask they can still answer, so
    /// they don't have to find the email. Null if they can't see the season, or it's finished.
    /// </summary>
    public async Task<MyBackups?> GetMineAsync(int contributorId, int year, CancellationToken cancellationToken = default)
    {
        if (await GetAvailabilityAsync(year, cancellationToken) is not { } availability || availability is BackupSignupAvailability.SeasonOver)
        {
            return null;
        }

        var membership = await GetMembershipAsync(contributorId, year, cancellationToken);
        var offers = membership is null
            ? []
            : await db.BackupOffers
                .AsNoTracking()
                .Where(o => o.Substitute.SeasonYear == year && o.Substitute.ContributorId == contributorId
                    && (o.Status == BackupOfferStatus.Asked || o.Status == BackupOfferStatus.Accepted))
                .OrderBy(o => o.Slot.Day).ThenBy(o => o.Slot.Part)
                .Select(o => new MyBackupOffer(o.Id, year, o.Slot.Day, o.Slot.Part, o.Slot.Season.SlotsPerDay, o.Status == BackupOfferStatus.Accepted))
                .ToListAsync(cancellationToken);

        return new MyBackups(year, availability, membership, [.. offers.Where(o => !schedule.IsDayOver(year, o.Day))]);
    }

    /// <summary>The seasons whose backup lists they're on.</summary>
    public async Task<IReadOnlyList<int>> GetListYearsAsync(int contributorId, CancellationToken cancellationToken = default) =>
        await db.Substitutes.Where(s => s.ContributorId == contributorId).Select(s => s.SeasonYear).Distinct().ToListAsync(cancellationToken);

    public async Task<BackupSignupOutcome> SignUpAsync(int contributorId, int year, BackupSignupForm form, CancellationToken cancellationToken = default)
    {
        if (await GetAvailabilityAsync(year, cancellationToken) is not BackupSignupAvailability.Open)
        {
            return BackupSignupOutcome.NotOpen;
        }

        var contributor = await db.Contributors.SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken);
        if (contributor is not { Email: { } email, EmailVerifiedAt: not null })
        {
            return BackupSignupOutcome.NoVerifiedEmail;
        }

        var season = await db.Seasons.SingleAsync(s => s.Year == year, cancellationToken);
        var substitute = new Substitute
        {
            ContributorId = contributorId,
            Name = form.Name.Trim(),
            Link = string.IsNullOrWhiteSpace(form.Link) ? null : form.Link.Trim(),
            Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim(),
            Email = email,
            SignedUpAt = timeProvider.GetUtcNow(),
        };
        season.Substitutes.Add(substitute);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return BackupSignupOutcome.AlreadyOnList;
        }

        // The email goes once per person and season, however often they leave and join again (#137). It's recorded first, so
        // two replicas can't both send it, and the record goes again if sending fails, so a later sign-up tries again. Someone
        // rejoining already has one, which is checked first: the unique index refusing it would be logged as an error (#157).
        if (await db.BackupSignupEmails.AnyAsync(e => e.SeasonYear == year && e.ContributorId == contributorId, cancellationToken))
        {
            return BackupSignupOutcome.Rejoined;
        }

        var record = new BackupSignupEmail { SeasonYear = year, ContributorId = contributorId, SentAt = timeProvider.GetUtcNow() };
        db.BackupSignupEmails.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            db.Entry(record).State = EntityState.Detached;
            return BackupSignupOutcome.Rejoined;
        }

        var position = (await GetMembershipAsync(contributorId, year, cancellationToken))?.Position ?? 1;
        if (!await composer.TrySendAsync(
            BackupEmails.SignedUp(substitute.Name, year, position, site.Value),
            new EmailRecipient(email, substitute.Name),
            $"the {year} backup list",
            cancellationToken))
        {
            db.BackupSignupEmails.Remove(record);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return BackupSignupOutcome.SignedUp;
    }

    /// <summary>Leaves the list. Any asks they had are dropped with them.</summary>
    public async Task LeaveAsync(int contributorId, int year, CancellationToken cancellationToken = default) =>
        await db.Substitutes
            .Where(s => s.SeasonYear == year && s.ContributorId == contributorId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<List<BackupListEntry>> ListAsync(int year, CancellationToken cancellationToken = default)
    {
        var list = await OrderedAsync(year, cancellationToken);
        var offers = await ListActiveOffersAsync(year, timeProvider.GetUtcNow(), cancellationToken);

        return [.. list.Select(s =>
        {
            var offer = offers.FirstOrDefault(o => o.SubstituteId == s.Id);
            var status = s.AssignedSlot is { } slot ? $"Filled December {slot.Day}"
                : offer is { StandingBy: true } ? $"Standing by for December {offer.Day}"
                : offer is not null ? $"Asked about December {offer.Day}"
                : "Waiting";
            return new BackupListEntry(PositionOf(list, s), s.Name, s.Contributor?.GithubLogin, s.Email, s.Notes, s.SignedUpAt, status);
        })];
    }

    /// <summary>
    /// A season's backups standing by, and those asked whose time to answer isn't up, in spot order: for the backup list's
    /// statuses, and the organizers' Today panel and morning digest (#171).
    /// </summary>
    public Task<List<ActiveBackupOffer>> ListActiveOffersAsync(int year, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        db.BackupOffers
            .AsNoTracking()
            .Where(o => o.Substitute.SeasonYear == year && (o.Status == BackupOfferStatus.Accepted || (o.Status == BackupOfferStatus.Asked && o.ExpiresAt > now)))
            .OrderBy(o => o.Slot.Day).ThenBy(o => o.Slot.Part).ThenBy(o => o.AskedAt).ThenBy(o => o.Id)
            .Select(o => new ActiveBackupOffer(
                o.SlotId,
                o.SubstituteId,
                o.Slot.Day,
                o.Slot.Part,
                o.Substitute.Name,
                o.Status == BackupOfferStatus.Accepted,
                o.AskedAt,
                o.ExpiresAt))
            .ToListAsync(cancellationToken);

    /// <summary>Every backup asked about a spot, in the order they were asked.</summary>
    public Task<List<SpotBackupOffer>> ListOffersAsync(int slotId, CancellationToken cancellationToken = default) =>
        db.BackupOffers
            .AsNoTracking()
            .Where(o => o.SlotId == slotId)
            .OrderBy(o => o.AskedAt).ThenBy(o => o.Id)
            .Select(o => new SpotBackupOffer(
                o.Substitute.Name,
                o.Substitute.Contributor == null ? null : o.Substitute.Contributor.GithubLogin,
                o.Substitute.Email,
                o.AskedAt,
                o.ExpiresAt,
                o.Status,
                o.RespondedAt,
                o.PostTitle,
                o.PostUrl))
            .ToListAsync(cancellationToken);

    private Task<List<Substitute>> OrderedAsync(int year, CancellationToken cancellationToken) =>
        db.Substitutes
            .AsNoTracking()
            .Include(s => s.Contributor)
            .Include(s => s.AssignedSlot)
            .Where(s => s.SeasonYear == year)
            .OrderBy(s => s.SignedUpAt).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Their place among the backups who haven't filled a spot yet, counting from 1.</summary>
    private static int? PositionOf(List<Substitute> list, Substitute substitute) =>
        substitute.AssignedSlotId is not null ? null : list.Where(s => s.AssignedSlotId is null).ToList().IndexOf(substitute) + 1;
}

public sealed class BackupSignupForm
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(50, ErrorMessage = "Keep your name to 50 characters or fewer.")]
    public string Name { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the link to 200 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Enter a full web address that starts with https:// or http://.")]
    [NotSocialProfile]
    public string? Link { get; set; }

    [StringLength(500, ErrorMessage = "Keep your notes to 500 characters or fewer.")]
    public string? Notes { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Agree to the terms to continue.")]
    public bool AgreeToTerms { get; set; }
}

/// <summary>Leaving the list needs a ticked confirmation.</summary>
public sealed class LeaveBackupListForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm you're leaving the backup list.")]
    public bool Confirm { get; set; }
}
