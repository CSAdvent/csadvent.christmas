using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Social.Posting;

public sealed record SocialPostingRun(int Posted, int Failed, int Unconfirmed)
{
    public static readonly SocialPostingRun None = new(0, 0, 0);

    public SocialPostingRun Add(PostOutcome outcome) => outcome switch
    {
        PostOutcome.Posted => this with { Posted = Posted + 1 },
        PostOutcome.Failed => this with { Failed = Failed + 1 },
        PostOutcome.Unconfirmed => this with { Unconfirmed = Unconfirmed + 1 },
        _ => this,
    };
}

public enum PostOutcome
{
    /// <summary>Already posted (by hand or automatically), being posted by another run, or waiting before its next try.</summary>
    Skipped,
    Posted,
    Failed,

    /// <summary>It may have gone out, but the platform never said (<see cref="SocialPostStatus.Unconfirmed"/>).</summary>
    Unconfirmed,
}

/// <summary>
/// Posts each revealed entry's announcement to every platform in Automatic mode (#61), at its time on the entry's day
/// (<see cref="SocialPostingOptions.PostAt"/>). <see cref="SocialPostingWorker"/> runs it every few minutes.
/// <list type="bullet">
/// <item>Exactly once, even with several replicas: a run claims an announcement by inserting its <c>social_posts</c> row
/// (unique per entry and platform) before posting it, so only one run posts it, and anything an organizer marked posted by
/// hand is skipped (<see cref="SocialPostAttempts"/>).</item>
/// <item>A failed post is tried again a few times, waiting longer each time, then waits for an organizer to retry it.</item>
/// <item>One that may have gone out without the platform saying so (a timeout once it was sent, say) is Unconfirmed, and waits
/// for an organizer to check the account, unless trying again can't post it twice (Bluesky), #154.</item>
/// <item>The organizers are emailed whenever a post stops and waits for them (#161).</item>
/// <item>Never the test season: its entries must never reach C# Advent's real accounts.</item>
/// <item>Only posts that passed the blurb check (#128), or that an organizer let through: a post that fails it, or hasn't been
/// checked, waits, and goes out as soon as it passes that day.</item>
/// </list>
/// </summary>
public sealed partial class SocialPostingService(
    AdventDbContext db,
    AdventSchedule schedule,
    SocialService social,
    SocialPlatformModes modes,
    SocialPublishers publishers,
    SocialPostingAlerts alerts,
    CardRenderer cards,
    IOptions<SocialPostingOptions> options,
    IOptions<SiteOptions> site,
    IOptions<SocialAccountsOptions> accounts,
    IWebHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<SocialPostingService> logger)
{
    // The general share card ("C# Advent, two C# posts a day"), for a test post's link card.
    private readonly Lazy<byte[]> _generalCard = new(() =>
    {
        using var stream = environment.WebRootFileProvider.GetFileInfo(CardLinks.General).CreateReadStream();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    });

    public const string Automation = "automation";

    public async Task<SocialPostingRun> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Whatever the modes: an organizer's retry can be interrupted too.
        await GiveUpOnInterruptedAsync(cancellationToken);

        var automatic = (await modes.GetAsync(cancellationToken)).Where(p => p.IsAutomatic).Select(p => p.Platform).ToList();
        if (automatic.Count == 0)
        {
            return SocialPostingRun.None;
        }

        var local = schedule.ToLocalTime(now).DateTime;
        var today = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        var years = await db.Seasons.Where(s => s.Year != Season.TestYear).Select(s => s.Year).ToListAsync(cancellationToken);

        var run = SocialPostingRun.None;
        foreach (var year in years)
        {
            if (schedule.GetDayOfAdvent(year, today) is not { } day || !schedule.IsRevealed(year, day, now))
            {
                continue;
            }

            var announcements = await social.GetDayAsync(year, day, now, cancellationToken);
            foreach (var entry in announcements.Entries.Where(e => time >= options.Value.TimeFor(e.Part) && e.CanAnnounce))
            {
                foreach (var platform in automatic)
                {
                    run = run.Add(await PostAsync(announcements, entry, platform, now, byOrganizer: false, cancellationToken));
                }
            }
        }

        return run;
    }

    /// <summary>
    /// Posts a failed or unconfirmed announcement again now, for an organizer, whatever its attempts. Returns null if there's
    /// no such automatic post for that entry and platform.
    /// </summary>
    public async Task<PostOutcome?> RetryAsync(int slotId, SocialPlatform platform, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots.AsNoTracking()
            .Where(s => s.Id == slotId && s.SeasonYear != Season.TestYear)
            .Select(s => new { s.SeasonYear, s.Day })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is null || !schedule.IsRevealed(slot.SeasonYear, slot.Day, now)
            || !await db.SocialPosts.AnyAsync(p => p.SlotId == slotId && p.Platform == platform
                && (p.Status == SocialPostStatus.Failed || p.Status == SocialPostStatus.Unconfirmed), cancellationToken))
        {
            return null;
        }

        var announcements = await social.GetDayAsync(slot.SeasonYear, slot.Day, now, cancellationToken);
        return announcements.Entries.SingleOrDefault(e => e.SlotId == slotId) is { } entry
            ? await PostAsync(announcements, entry, platform, now, byOrganizer: true, cancellationToken)
            : null;
    }

    /// <summary>
    /// Posts a test post on the platform for an organizer to see: formatted exactly as automatic posting formats an
    /// announcement, but plainly a test, about no entry, linking to a page of its own (<see cref="Announcement.Test"/>). It's a
    /// real, public post, so it's for checking before the season. Returns the platform's error, or null if it went out.
    /// </summary>
    public async Task<string?> SendTestAsync(SocialPlatform platform, string sentBy, CancellationToken cancellationToken = default)
    {
        if (publishers.For(platform) is not { } publisher)
        {
            return "There are no credentials for it.";
        }

        var id = Guid.NewGuid().ToString("N")[..12];
        var test = Announcement.Test(schedule.Today.Year, site.Value.BaseUrl, id);
        var publication = new SocialPublication(
            test.Year,
            test.Day,
            test.Part,
            "C# Advent test post",
            "Checking that C# Advent can post here automatically.",
            AnnouncementFormatter.Format(test, platform, accounts.Value),
            _generalCard.Value)
        {
            Key = $"csadvent-test-{id}",
        };

        var attempt = await SocialPostAttempts.TryAsync(publisher, publication, cancellationToken);
        await modes.RecordTestAsync(platform, attempt.PostUrl, attempt.Error, sentBy, cancellationToken);
        return attempt.Error;
    }

    /// <summary>How long a failed post waits before its next try: 5 minutes, then 10, then 20.</summary>
    public static TimeSpan WaitAfter(int attempts) => TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Max(0, attempts - 1)));

    private async Task<PostOutcome> PostAsync(AnnouncementDay day, EntryAnnouncements entry, SocialPlatform platform, DateTimeOffset now, bool byOrganizer, CancellationToken cancellationToken)
    {
        if (publishers.For(platform) is not { } publisher || entry.Platforms.SingleOrDefault(p => p.Platform == platform) is not { } announcement)
        {
            return PostOutcome.Skipped;
        }

        if (await ClaimAsync(entry.SlotId, platform, now, byOrganizer, cancellationToken) is not { } attempts)
        {
            return PostOutcome.Skipped;
        }

        var attempt = await SocialPostAttempts.TryAsync(publisher, Publication(day, entry, announcement), cancellationToken);
        var status = SocialPostAttempts.StatusAfter(attempt, publisher.RetriesAreSafe);
        if (status is SocialPostStatus.Failed or SocialPostStatus.Unconfirmed)
        {
            LogFailed(attempt.Exception, platform, entry.SlotId);
        }

        var recorded = await SocialPostAttempts.RecordAsync(
            db.SocialPosts.Where(p => p.SlotId == entry.SlotId && p.Platform == platform), status, attempt.PostUrl, attempt.Error, now, cancellationToken);

        // It stops here, waiting for an organizer: tell them, unless it was their own retry, whose result they're looking at.
        var stopped = status is SocialPostStatus.Unconfirmed || (status is SocialPostStatus.Failed && attempts >= options.Value.MaxAttempts);
        if (recorded && stopped && !byOrganizer)
        {
            await AlertAsync(entry.SlotId, platform, status is SocialPostStatus.Unconfirmed, attempt.Error!, attempts, cancellationToken);
        }

        return status switch
        {
            SocialPostStatus.Posted => PostOutcome.Posted,
            SocialPostStatus.Unconfirmed => PostOutcome.Unconfirmed,
            _ => PostOutcome.Failed,
        };
    }

    /// <summary>
    /// Claims the announcement for this run: a new row, or a failed one that's due another try (or that an organizer is
    /// retrying, as they can an unconfirmed one). Whoever claims it posts it, and everyone else skips it. Returns which try
    /// it is.
    /// </summary>
    private async Task<int?> ClaimAsync(int slotId, SocialPlatform platform, DateTimeOffset now, bool byOrganizer, CancellationToken cancellationToken)
    {
        var existing = await db.SocialPosts.AsNoTracking()
            .Where(p => p.SlotId == slotId && p.Platform == platform)
            .Select(p => new { p.Status, p.Attempts, p.At })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            var claim = new SocialPost { SlotId = slotId, Platform = platform, Status = SocialPostStatus.Posting, At = now, MarkedBy = Automation, Attempts = 1 };
            return await SocialPostAttempts.TryClaimAsync(db, claim, cancellationToken) ? 1 : null;
        }

        var due = existing.Status switch
        {
            SocialPostStatus.Failed => byOrganizer || (existing.Attempts < options.Value.MaxAttempts && now >= existing.At + WaitAfter(existing.Attempts)),
            SocialPostStatus.Unconfirmed => byOrganizer,
            _ => false,
        };
        if (!due)
        {
            return null;
        }

        // Only the run that moves it on (with the status and attempts it read) gets it.
        var attempts = byOrganizer ? 1 : existing.Attempts + 1;
        return await db.SocialPosts
            .Where(p => p.SlotId == slotId && p.Platform == platform && p.Status == existing.Status && p.Attempts == existing.Attempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SocialPostStatus.Posting)
                .SetProperty(p => p.Attempts, attempts)
                .SetProperty(p => p.At, now)
                .SetProperty(p => p.MarkedBy, Automation), cancellationToken) == 1
            ? attempts
            : null;
    }

    private async Task GiveUpOnInterruptedAsync(CancellationToken cancellationToken)
    {
        foreach (var post in await SocialPostAttempts.GiveUpOnInterruptedAsync(db.SocialPosts, timeProvider.GetUtcNow(), cancellationToken))
        {
            await AlertAsync(post.SlotId, post.Platform, mayHavePosted: true, SocialPostAttempts.Interrupted, post.Attempts, cancellationToken);
        }
    }

    private async Task AlertAsync(int slotId, SocialPlatform platform, bool mayHavePosted, string error, int attempts, CancellationToken cancellationToken)
    {
        var slot = await db.Slots.AsNoTracking()
            .Where(s => s.Id == slotId)
            .Select(s => new { s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.PostTitle })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is not null)
        {
            await alerts.StoppedAsync(
                StoppedPost.Entry(platform, slot.SeasonYear, slot.Day, slot.Part, slot.SlotsPerDay, slot.PostTitle, mayHavePosted, error, attempts),
                cancellationToken);
        }
    }

    private SocialPublication Publication(AnnouncementDay day, EntryAnnouncements entry, FormattedAnnouncement announcement) => new(
        day.Year,
        day.Day,
        entry.Part,
        entry.Title,
        Description(day, entry),
        announcement,
        cards.RenderEntry(new EntryCard(day.Year, day.Day, entry.Part, day.SlotsPerDay, entry.Title, entry.Author)));

    /// <summary>What a link card says under the title, like the entry page's preview: the post's summary first, if it has one.</summary>
    public static string Description(AnnouncementDay day, EntryAnnouncements entry)
    {
        var byline = day.SlotsPerDay > 1
            ? $"By {entry.Author}, for C# Advent {day.Year}: December {day.Day}, post {entry.Part}."
            : $"By {entry.Author}, for C# Advent {day.Year}: December {day.Day}.";
        return entry.Summary is { } summary ? $"{PostSummaries.AsSentence(summary)} {byline}" : byline;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting slot {SlotId} to {Platform} failed; it'll be tried again or shown to the organizers")]
    private partial void LogFailed(Exception? exception, SocialPlatform platform, int slotId);
}
