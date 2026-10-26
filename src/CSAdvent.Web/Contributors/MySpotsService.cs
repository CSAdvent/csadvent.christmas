using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Contributors;

/// <param name="IsRevealed">Whether its day is revealed, so its visits and clicks count.</param>
public sealed record MySpot(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string? PostTitle, string? PostUrl, EntryStatus Status, bool IsDayOver, bool IsRevealed, PostTraffic Traffic)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    public required SpotDeadlines Deadlines { get; init; }
}

/// <summary>What happens when, for one spot, so its author knows their deadlines (#175). All follow a rehearsal.</summary>
/// <param name="AskBackupsFrom">The evening before: if its post's link is still missing, backup posters are asked to stand by.</param>
/// <param name="CheckBlurbFrom">When the blurb check first reads the post, in seasons with the rule.</param>
/// <param name="RevealsAt">Midnight on its day. A backup who said yes takes a spot that still has no link then.</param>
/// <param name="AnnouncedAt">When it's announced on social media: the deadline for its blurb.</param>
public sealed record SpotDeadlines(DateTimeOffset AskBackupsFrom, DateTimeOffset CheckBlurbFrom, DateTimeOffset RevealsAt, DateTimeOffset AnnouncedAt);

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

    /// <summary>Its post is revealed, so a new link waits for an organizer (#155).</summary>
    public bool IsPublished { get; init; }

    /// <summary>The new link they've asked for, while it waits for an organizer.</summary>
    public string? PendingLink { get; init; }
}

public enum MySpotOutcome
{
    Saved,

    /// <summary>Saved, and the new link is waiting for an organizer (a revealed post's, #155).</summary>
    LinkRequested,
    DroppedOut,
    ChangedByOthers,
    TooLate,
    NotFound,

    /// <summary>A revealed post can't be taken off the calendar here; they can drop out instead.</summary>
    PostRequired,

    /// <summary>Nothing saved: the new link can't be asked for, because they have a request for new links waiting already.</summary>
    LinkAlreadyRequested,

    /// <summary>Nothing saved: they've asked for new links too often in the last hour.</summary>
    LinkTooMany,

    /// <summary>Nothing saved: a new link needs a verified email, so we can tell them what the organizers decide.</summary>
    NoVerifiedEmail,
}

/// <summary>
/// A contributor's own spots: adding their post's title and URL (that's how posts get onto the calendar), fixing their
/// name and link, and dropping out. Only seasons that aren't finished yet are shown.
/// </summary>
/// <remarks>
/// <para>
/// A spot is the contributor's if it's linked to their account, or if it isn't linked to anyone and its contact email is
/// their verified GitHub email (spots an organizer added by hand). Saving links such a spot to them.
/// </para>
/// <para>
/// Once a post is revealed (#155), every link we share goes to it, so a new link waits for an organizer (a post link
/// request), and a new title or summary shows straight away with an email to the organizers, in case it needs putting back.
/// The test season saves straight away, since nothing in it is published.
/// </para>
/// </remarks>
public sealed partial class MySpotsService(
    AdventDbContext db,
    AdventSchedule schedule,
    TimeProvider timeProvider,
    EmailComposer composer,
    IEmailSender sender,
    BackupService backups,
    PostLinkService postLinks,
    IOptions<SiteOptions> site,
    IOptions<SocialPostingOptions> posting,
    IOptions<BlurbOptions> blurbOptions,
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
                .Where(s => !schedule.IsFinished(s.SeasonYear))
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
                    traffic.GetValueOrDefault(s.Id, PostTraffic.None))
                {
                    Deadlines = DeadlinesOf(s.SeasonYear, s.Day, s.Part),
                })],
            [.. requests.Where(r => !schedule.IsFinished(r.Year))]);
    }

    /// <summary>A spot's deadlines: when backups could be asked, when its blurb is checked, and when it's revealed and announced.</summary>
    public SpotDeadlines DeadlinesOf(int year, int day, int part)
    {
        var revealsAt = schedule.GetRevealTime(year, day);
        return new SpotDeadlines(backups.GetAskStart(year, day), revealsAt - blurbOptions.Value.CheckFrom, revealsAt, BlurbRules.Deadline(schedule, posting.Value, year, day, part));
    }

    /// <summary>
    /// The contributor's spots and pending requests in a season, marked on its calendar for them (#175). It's only ever their
    /// own, so it says nothing about anyone else's spot, or about a post before its reveal.
    /// </summary>
    public async Task<CalendarMarks> GetCalendarMarksAsync(int contributorId, int year, CancellationToken cancellationToken = default)
    {
        var email = await VerifiedEmailAsync(contributorId, cancellationToken);
        var mine = await Mine(contributorId, email)
            .Where(s => s.SeasonYear == year)
            .Select(s => new { s.Day, s.Part })
            .ToListAsync(cancellationToken);
        var requested = await db.ClaimRequests
            .Where(r => r.ContributorId == contributorId && r.Status == ClaimRequestStatus.Pending && r.Slot.SeasonYear == year)
            .Select(r => new { r.Slot.Day, r.Slot.Part })
            .ToListAsync(cancellationToken);

        return new CalendarMarks(mine.Select(s => (s.Day, s.Part)).ToHashSet(), requested.Select(r => (r.Day, r.Part)).ToHashSet());
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
            .Where(p => schedule.IsFinished(p.SeasonYear))
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
        if (slot is null || schedule.IsFinished(slot.SeasonYear))
        {
            return null;
        }

        var isPublished = IsPublished(slot);
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
            })
        {
            IsPublished = isPublished,
            PendingLink = isPublished ? await postLinks.GetPendingLinkAsync(contributorId, slot.Id, cancellationToken) : null,
        };
    }

    public async Task<MySpotOutcome> SaveAsync(int contributorId, int slotId, MySpotForm form, CancellationToken cancellationToken = default)
    {
        var slot = await LoadForUpdateAsync(contributorId, slotId, form.Version, cancellationToken);
        if (slot is null)
        {
            return MySpotOutcome.NotFound;
        }

        var (title, url, summary) = (Clean(form.PostTitle), Clean(form.PostUrl), PostSummaries.Clean(form.PostSummary));

        // A spot that never got a post can't get one once its day is over, when it's no longer announced or checked.
        if (schedule.IsFinished(slot.SeasonYear) || (slot.PostUrl is null && url is not null && schedule.IsDayOver(slot.SeasonYear, slot.Day)))
        {
            return MySpotOutcome.TooLate;
        }

        var isPublished = IsPublished(slot);
        if (isPublished && url is null)
        {
            return MySpotOutcome.PostRequired;
        }

        // From the reveal, a new link waits for an organizer, so check first that they can ask for one.
        var newLink = isPublished && url != slot.PostUrl ? url : null;
        if (newLink is not null && await postLinks.CheckRequesterAsync(contributorId, cancellationToken) is { } refused)
        {
            return Outcome(refused);
        }

        var (oldTitle, oldSummary) = (slot.PostTitle, slot.PostSummary);
        slot.AuthorName = form.AuthorName.Trim();
        slot.AuthorUrl = Clean(form.AuthorUrl);
        slot.PostTitle = title;
        slot.PostUrl = newLink is null ? url : slot.PostUrl;
        slot.PostSummary = summary;
        slot.ContributorId = contributorId;

        var outcome = await SaveChangesAsync(MySpotOutcome.Saved, cancellationToken);
        if (outcome is not MySpotOutcome.Saved || !isPublished)
        {
            return outcome;
        }

        if (title != oldTitle || summary != oldSummary)
        {
            var change = new PostChangeDetails(
                slot.Id, slot.AuthorName, slot.SeasonYear, slot.Day, slot.Part, await SlotsPerDayAsync(slot.SeasonYear, cancellationToken), oldTitle, title, oldSummary, summary);
            await TellOrganizersAsync(SpotEmails.PostChanged(change, site.Value), "post change", slot, cancellationToken);
        }

        return newLink is null ? MySpotOutcome.Saved : Outcome(await postLinks.RequestNewLinkAsync(contributorId, slot.Id, newLink, cancellationToken));
    }

    /// <summary>Whether the spot's post is out: revealed, with a link. The test season's never is, since nothing in it is published.</summary>
    private bool IsPublished(Slot slot) => slot.PostUrl is not null && slot.SeasonYear != Season.TestYear && schedule.IsRevealed(slot);

    private static MySpotOutcome Outcome(PostLinkOutcome link) => link switch
    {
        PostLinkOutcome.Requested => MySpotOutcome.LinkRequested,
        PostLinkOutcome.TooMany => MySpotOutcome.LinkTooMany,
        PostLinkOutcome.NoVerifiedEmail => MySpotOutcome.NoVerifiedEmail,
        _ => MySpotOutcome.LinkAlreadyRequested,
    };

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
            await TellOrganizersAsync(SpotEmails.DroppedOut(dropout, site.Value), "dropout", slot, cancellationToken);
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

    private async Task TellOrganizersAsync(EmailContent content, string kind, Slot slot, CancellationToken cancellationToken)
    {
        try
        {
            var email = await composer.ComposeAsync(content, new EmailRecipient(site.Value.ContactEmail, "C# Advent organizers"));
            await sender.SendAsync(email, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogOrganizerEmailFailed(exception, kind, slot.SeasonYear, slot.Day, slot.Part);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the {Kind} email telling the organizers about December {Day}, {Year}, part {Part}")]
    private partial void LogOrganizerEmailFailed(Exception exception, string kind, int year, int day, int part);
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
