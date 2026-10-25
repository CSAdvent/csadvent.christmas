using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Backups;

public enum BackupSignupAvailability
{
    Open,
    NotOpen,
    SeasonOver,
}

/// <summary>Someone's place on a season's backup list. <see cref="FilledSpot"/> is set once they've filled one.</summary>
public sealed record BackupMembership(int? Position, string? FilledSpot);

public sealed record BackupListEntry(
    int? Position,
    string Name,
    string? GithubLogin,
    string Email,
    string? Notes,
    DateTimeOffset SignedUpAt,
    string Status);

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
public sealed partial class BackupListService(
    AdventDbContext db,
    AdventSchedule schedule,
    TimeProvider timeProvider,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<SiteOptions> site,
    SeasonAccess access,
    ILogger<BackupListService> logger)
{
    public async Task<BackupSignupAvailability?> GetAvailabilityAsync(int year, CancellationToken cancellationToken = default)
    {
        var season = access.CanSee(year) ? await db.Seasons.AsNoTracking().SingleOrDefaultAsync(s => s.Year == year, cancellationToken) : null;
        return season is null ? null
            : schedule.IsDayOver(year, Season.DaysInAdvent) ? BackupSignupAvailability.SeasonOver
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
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return BackupSignupOutcome.AlreadyOnList;
        }

        // The email goes once per person and season, however often they leave and join again (#137). It's recorded first, so
        // two replicas can't both send it, and the record goes again if sending fails, so a later sign-up tries again.
        var record = new BackupSignupEmail { SeasonYear = year, ContributorId = contributorId, SentAt = timeProvider.GetUtcNow() };
        db.BackupSignupEmails.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(record).State = EntityState.Detached;
            return BackupSignupOutcome.Rejoined;
        }

        var position = (await GetMembershipAsync(contributorId, year, cancellationToken))?.Position ?? 1;
        try
        {
            var message = await composer.ComposeAsync(
                BackupEmails.SignedUp(substitute.Name, year, position, site.Value),
                new EmailRecipient(email, substitute.Name));
            await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSignupEmailFailed(exception, year);
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
        var now = timeProvider.GetUtcNow();
        var offers = await db.BackupOffers
            .AsNoTracking()
            .Where(o => o.Substitute.SeasonYear == year && (o.Status == BackupOfferStatus.Accepted || (o.Status == BackupOfferStatus.Asked && o.ExpiresAt > now)))
            .Select(o => new { o.SubstituteId, o.Status, o.Slot.Day })
            .ToListAsync(cancellationToken);

        return [.. list.Select(s =>
        {
            var offer = offers.FirstOrDefault(o => o.SubstituteId == s.Id);
            var status = s.AssignedSlot is { } slot ? $"Filled December {slot.Day}"
                : offer is { Status: BackupOfferStatus.Accepted } ? $"Standing by for December {offer.Day}"
                : offer is not null ? $"Asked about December {offer.Day}"
                : "Waiting";
            return new BackupListEntry(PositionOf(list, s), s.Name, s.Contributor?.GithubLogin, s.Email, s.Notes, s.SignedUpAt, status);
        })];
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send the backup sign-up email for {Year}")]
    private partial void LogSignupEmailFailed(Exception exception, int year);
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
