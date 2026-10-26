using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Contributors;

/// <param name="IsRevealed">Whether its day is revealed, so its visits and clicks count.</param>
public sealed record MySpot(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string? PostTitle, string? PostUrl, EntryStatus Status, bool IsDayOver, bool IsRevealed, PostTraffic Traffic)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

/// <param name="SpotTaken">The spot has gone to someone else while the request waits for the organizers.</param>
public sealed record MyRequest(int Id, int Year, int Day, int Part, int SlotsPerDay, DateTimeOffset SubmittedAt, bool SpotTaken)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

public sealed record MySpotsOverview(IReadOnlyList<MySpot> Spots, IReadOnlyList<MyRequest> PendingRequests);

/// <summary>
/// How a post did: visits to its entry page and clicks through to the post, counted since 2026 without bots or link previews.
/// Only its author sees these for their own posts; the public only sees season totals and the most-read posts.
/// </summary>
public sealed record PostTraffic(int Views, int Clicks)
{
    public static readonly PostTraffic None = new(0, 0);

    public bool Any => Views + Clicks > 0;
}

/// <summary>A post the contributor published in a finished season, linked to their entry page.</summary>
public sealed record PastPost(int Year, int Day, int Part, int SlotsPerDay, string Title, PostTraffic Traffic)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day} · Post {Part}" : $"December {Day}";

    public string EntryPage => EntryLinks.Page(Year, Day, Part);
}

public sealed record MySpotDetails(int SlotId, int Year, int Day, int Part, int SlotsPerDay, bool IsRevealed, bool IsDayOver, uint Version, MySpotForm Values)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

public enum MySpotOutcome
{
    Saved,
    DroppedOut,
    ChangedByOthers,
    TooLate,
    NotFound,
}

/// <summary>
/// A contributor's own spots: adding their post's title and URL (that's how posts get onto the calendar), fixing their
/// name and link, and dropping out. Only seasons that aren't over yet are shown.
/// </summary>
/// <remarks>
/// A spot is the contributor's if it's linked to their account, or if it isn't linked to anyone and its contact email is
/// their verified GitHub email (spots an organizer added by hand). Saving links such a spot to them.
/// </remarks>
public sealed partial class MySpotsService(
    AdventDbContext db,
    AdventSchedule schedule,
    TimeProvider timeProvider,
    EmailComposer composer,
    IEmailSender sender,
    BackupService backups,
    IOptions<SiteOptions> site,
    ILogger<MySpotsService> logger)
{
    public async Task<MySpotsOverview> GetOverviewAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        var email = await VerifiedEmailAsync(contributorId, cancellationToken);
        var spots = await Mine(contributorId, email)
            .AsNoTracking()
            .OrderBy(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new { s.Id, s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.AuthorName, s.PostTitle, s.PostUrl })
            .ToListAsync(cancellationToken);

        var traffic = await TrafficAsync([.. spots.Select(s => s.Id)], cancellationToken);
        var requests = await db.ClaimRequests
            .AsNoTracking()
            .Where(r => r.ContributorId == contributorId && r.Status == ClaimRequestStatus.Pending)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new MyRequest(r.Id, r.Slot.SeasonYear, r.Slot.Day, r.Slot.Part, r.Slot.Season.SlotsPerDay, r.SubmittedAt, r.Slot.AuthorName != null))
            .ToListAsync(cancellationToken);

        return new MySpotsOverview(
            [.. spots
                .Where(s => !schedule.IsSeasonOver(s.SeasonYear))
                .Select(s => new MySpot(
                    s.Id,
                    s.SeasonYear,
                    s.Day,
                    s.Part,
                    s.SlotsPerDay,
                    s.PostTitle,
                    s.PostUrl,
                    EntryAdminService.StatusOf(s.AuthorName, s.PostTitle, s.PostUrl),
                    schedule.IsDayOver(s.SeasonYear, s.Day),
                    schedule.IsRevealed(s.SeasonYear, s.Day),
                    traffic.GetValueOrDefault(s.Id, PostTraffic.None)))],
            [.. requests.Where(r => !schedule.IsSeasonOver(r.Year))]);
    }

    /// <summary>
    /// Every post linked to the contributor in a season that's over, newest season first: their C# Advent history. Posts from
    /// 2026 on are linked when their claim is approved, and organizers link earlier ones (<see cref="AuthorService"/>).
    /// </summary>
    public async Task<IReadOnlyList<PastPost>> GetHistoryAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => s.ContributorId == contributorId && s.SeasonYear != Season.TestYear && s.PostUrl != null && s.AuthorName != null)
            .Select(s => new { s.Id, s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.PostTitle })
            .ToListAsync(cancellationToken);
        var traffic = await TrafficAsync([.. posts.Select(p => p.Id)], cancellationToken);

        return [.. posts
            .Where(p => schedule.IsSeasonOver(p.SeasonYear))
            .OrderByDescending(p => p.SeasonYear).ThenBy(p => p.Day).ThenBy(p => p.Part)
            .Select(p => new PastPost(p.SeasonYear, p.Day, p.Part, p.SlotsPerDay, p.PostTitle ?? "Untitled", traffic.GetValueOrDefault(p.Id, PostTraffic.None)))];
    }

    public async Task<MySpotDetails?> GetAsync(int contributorId, int slotId, CancellationToken cancellationToken = default)
    {
        var email = await VerifiedEmailAsync(contributorId, cancellationToken);
        var slot = await Mine(contributorId, email)
            .AsNoTracking()
            .Include(s => s.Season)
            .SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);
        if (slot is null || schedule.IsSeasonOver(slot.SeasonYear))
        {
            return null;
        }

        return new MySpotDetails(
            slot.Id,
            slot.SeasonYear,
            slot.Day,
            slot.Part,
            slot.Season.SlotsPerDay,
            schedule.IsRevealed(slot),
            schedule.IsDayOver(slot.SeasonYear, slot.Day),
            slot.Version,
            new MySpotForm
            {
                AuthorName = slot.AuthorName!,
                AuthorUrl = slot.AuthorUrl,
                PostTitle = slot.PostTitle,
                PostUrl = slot.PostUrl,
                PostSummary = slot.PostSummary,
                Version = slot.Version,
            });
    }

    public async Task<MySpotOutcome> SaveAsync(int contributorId, int slotId, MySpotForm form, CancellationToken cancellationToken = default)
    {
        var slot = await LoadForUpdateAsync(contributorId, slotId, form.Version, cancellationToken);
        if (slot is null)
        {
            return MySpotOutcome.NotFound;
        }

        if (schedule.IsSeasonOver(slot.SeasonYear))
        {
            return MySpotOutcome.TooLate;
        }

        slot.AuthorName = form.AuthorName.Trim();
        slot.AuthorUrl = Clean(form.AuthorUrl);
        slot.PostTitle = Clean(form.PostTitle);
        slot.PostUrl = Clean(form.PostUrl);
        slot.PostSummary = PostSummaries.Clean(form.PostSummary);
        slot.ContributorId = contributorId;

        return await SaveChangesAsync(MySpotOutcome.Saved, cancellationToken);
    }

    /// <summary>
    /// Gives the spot up: everything about the author is cleared, so they're never named, and the spot is marked dropped out
    /// so the backup posters are asked to fill it. Allowed until the day is over.
    /// </summary>
    public async Task<MySpotOutcome> DropOutAsync(int contributorId, int slotId, uint version, CancellationToken cancellationToken = default)
    {
        var slot = await LoadForUpdateAsync(contributorId, slotId, version, cancellationToken);
        if (slot is null)
        {
            return MySpotOutcome.NotFound;
        }

        if (schedule.IsDayOver(slot.SeasonYear, slot.Day))
        {
            return MySpotOutcome.TooLate;
        }

        var dropout = new DropoutDetails(slot.AuthorName!, slot.SeasonYear, slot.Day, slot.Part, await SlotsPerDayAsync(slot.SeasonYear, cancellationToken));
        slot.AuthorName = null;
        slot.AuthorUrl = null;
        slot.PostTitle = null;
        slot.PostUrl = null;
        slot.PostSummary = null;
        slot.ContributorId = null;
        slot.DroppedOutAt = timeProvider.GetUtcNow();
        if (slot.Contact is not null)
        {
            db.SlotContacts.Remove(slot.Contact);
        }

        var outcome = await SaveChangesAsync(MySpotOutcome.DroppedOut, cancellationToken);
        if (outcome is MySpotOutcome.DroppedOut)
        {
            await TellOrganizersAsync(dropout, cancellationToken);
            await backups.OfferNowAsync(slotId, timeProvider.GetUtcNow(), cancellationToken);
        }

        return outcome;
    }

    /// <summary>Each spot's visits and clicks, for its author.</summary>
    private async Task<Dictionary<int, PostTraffic>> TrafficAsync(List<int> slotIds, CancellationToken cancellationToken)
    {
        if (slotIds.Count == 0)
        {
            return [];
        }

        var counts = await db.EntryHits
            .Where(h => slotIds.Contains(h.SlotId))
            .GroupBy(h => new { h.SlotId, h.Kind })
            .Select(g => new { g.Key.SlotId, g.Key.Kind, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts
            .GroupBy(c => c.SlotId)
            .ToDictionary(
                g => g.Key,
                g => new PostTraffic(g.Where(c => c.Kind == EntryHitKind.View).Sum(c => c.Count), g.Where(c => c.Kind == EntryHitKind.Click).Sum(c => c.Count)));
    }

    private IQueryable<Slot> Mine(int contributorId, string? verifiedEmail) =>
        db.Slots.Where(s => s.AuthorName != null
            && (s.ContributorId == contributorId
                || (s.ContributorId == null && verifiedEmail != null && s.Contact != null && s.Contact.Email.ToLower() == verifiedEmail)));

    private async Task<string?> VerifiedEmailAsync(int contributorId, CancellationToken cancellationToken)
    {
        var email = await db.Contributors
            .Where(c => c.Id == contributorId && c.EmailVerifiedAt != null)
            .Select(c => c.Email)
            .SingleOrDefaultAsync(cancellationToken);
        return email?.ToLowerInvariant();
    }

    private async Task<Slot?> LoadForUpdateAsync(int contributorId, int slotId, uint version, CancellationToken cancellationToken)
    {
        var email = await VerifiedEmailAsync(contributorId, cancellationToken);
        var slot = await Mine(contributorId, email).Include(s => s.Contact).SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);
        if (slot is not null)
        {
            // Compare against the version the contributor saw, so an organizer's edit in between isn't overwritten.
            db.Entry(slot).Property(s => s.Version).OriginalValue = version;
        }

        return slot;
    }

    private Task<int> SlotsPerDayAsync(int year, CancellationToken cancellationToken) =>
        db.Seasons.Where(s => s.Year == year).Select(s => s.SlotsPerDay).SingleAsync(cancellationToken);

    private async Task<MySpotOutcome> SaveChangesAsync(MySpotOutcome success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return MySpotOutcome.ChangedByOthers;
        }
    }

    private async Task TellOrganizersAsync(DropoutDetails dropout, CancellationToken cancellationToken)
    {
        try
        {
            var email = await composer.ComposeAsync(
                SpotEmails.DroppedOut(dropout, site.Value),
                new EmailRecipient(site.Value.ContactEmail, "C# Advent organizers"));
            await sender.SendAsync(email, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDropoutEmailFailed(exception, dropout.Year, dropout.Day, dropout.Part);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the email telling the organizers that December {Day}, {Year}, part {Part} was dropped")]
    private partial void LogDropoutEmailFailed(Exception exception, int year, int day, int part);
}

/// <summary>A contributor's edit of their own spot. Their name is required; the post's title and URL can come later.</summary>
public sealed class MySpotForm : IValidatableObject
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(50, ErrorMessage = "Keep your name to 50 characters or fewer.")]
    public string AuthorName { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the link to 200 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Your website must be a full web address that starts with https:// or http://.")]
    [NotSocialProfile]
    public string? AuthorUrl { get; set; }

    [StringLength(300, ErrorMessage = "Keep the title to 300 characters or fewer.")]
    public string? PostTitle { get; set; }

    [StringLength(500, ErrorMessage = "Keep the URL to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Your post's URL must be a full web address that starts with https:// or http://.")]
    public string? PostUrl { get; set; }

    /// <summary>Optional: the post in one line, for its page, the feeds and our social posts once it's revealed.</summary>
    [StringLength(Slot.PostSummaryMaxLength, ErrorMessage = "Keep the summary to 160 characters or fewer.")]
    public string? PostSummary { get; set; }

    /// <summary>The spot's row version when the form was shown.</summary>
    public uint Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The calendar shows the title as a link, so it needs both.
        if (string.IsNullOrWhiteSpace(PostTitle) != string.IsNullOrWhiteSpace(PostUrl))
        {
            yield return new ValidationResult(
                "Add both your post's title and its URL, so the calendar can link the title to your post.",
                [string.IsNullOrWhiteSpace(PostTitle) ? nameof(PostTitle) : nameof(PostUrl)]);
        }
    }
}

/// <summary>Dropping out needs a ticked confirmation, so it can't happen by accident.</summary>
public sealed class DropOutForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm you're giving up this spot.")]
    public bool Confirm { get; set; }

    public uint Version { get; set; }
}
