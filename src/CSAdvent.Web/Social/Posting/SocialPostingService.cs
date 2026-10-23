using System.Text.Json;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Social.Posting;

public sealed record SocialPostingRun(int Posted, int Failed)
{
    public static readonly SocialPostingRun None = new(0, 0);

    public SocialPostingRun Add(PostOutcome outcome) => outcome switch
    {
        PostOutcome.Posted => this with { Posted = Posted + 1 },
        PostOutcome.Failed => this with { Failed = Failed + 1 },
        _ => this,
    };
}

public enum PostOutcome
{
    /// <summary>Already posted (by hand or automatically), being posted by another run, or waiting before its next try.</summary>
    Skipped,
    Posted,
    Failed,
}

/// <summary>
/// Posts each revealed entry's announcement to every platform in Automatic mode (#61), at its time on the entry's day
/// (<see cref="SocialPostingOptions.PostAt"/>). <see cref="SocialPostingWorker"/> runs it every few minutes.
/// <list type="bullet">
/// <item>Exactly once, even with several replicas: a run claims an announcement by inserting its <c>social_posts</c> row
/// (unique per entry and platform) before posting it, so only one run posts it, and anything an organizer marked posted by
/// hand is skipped.</item>
/// <item>A failed post is tried again a few times, waiting longer each time, then waits for an organizer to retry it.</item>
/// <item>Never the test season: its entries must never reach C# Advent's real accounts.</item>
/// </list>
/// </summary>
public sealed partial class SocialPostingService(
    AdventDbContext db,
    AdventSchedule schedule,
    SocialService social,
    SocialPlatformModes modes,
    SocialPublishers publishers,
    CardRenderer cards,
    IOptions<SocialPostingOptions> options,
    ILogger<SocialPostingService> logger)
{
    public const string Automation = "automation";

    /// <summary>A post still marked as posting after this long was interrupted (a restart, say), and won't be tried again by itself.</summary>
    public static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(15);

    public async Task<SocialPostingRun> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var automatic = (await modes.GetAsync(cancellationToken)).Where(p => p.IsAutomatic).Select(p => p.Platform).ToList();
        if (automatic.Count == 0)
        {
            return SocialPostingRun.None;
        }

        await GiveUpOnInterruptedAsync(now, cancellationToken);

        var local = schedule.ToLocalTime(now).DateTime;
        var today = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        var years = await db.Seasons.Where(s => s.Year != Season.TestYear).Select(s => s.Year).ToListAsync(cancellationToken);

        var run = SocialPostingRun.None;
        foreach (var year in years)
        {
            if (schedule.GetDayOfAdvent(year, today) is not { } day || !schedule.IsRevealed(year, day))
            {
                continue;
            }

            var announcements = await social.GetDayAsync(year, day, cancellationToken);
            foreach (var entry in announcements.Entries.Where(e => time >= options.Value.TimeFor(e.Part)))
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
    /// Posts a failed announcement again now, for an organizer, whatever its attempts. Returns null if there's no failed
    /// automatic post for that entry and platform.
    /// </summary>
    public async Task<PostOutcome?> RetryAsync(int slotId, SocialPlatform platform, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots.AsNoTracking()
            .Where(s => s.Id == slotId && s.SeasonYear != Season.TestYear)
            .Select(s => new { s.SeasonYear, s.Day })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is null || !schedule.IsRevealed(slot.SeasonYear, slot.Day)
            || !await db.SocialPosts.AnyAsync(p => p.SlotId == slotId && p.Platform == platform && p.Status == SocialPostStatus.Failed, cancellationToken))
        {
            return null;
        }

        var announcements = await social.GetDayAsync(slot.SeasonYear, slot.Day, cancellationToken);
        return announcements.Entries.SingleOrDefault(e => e.SlotId == slotId) is { } entry
            ? await PostAsync(announcements, entry, platform, now, byOrganizer: true, cancellationToken)
            : null;
    }

    /// <summary>How long a failed post waits before its next try: 5 minutes, then 10, then 20.</summary>
    public static TimeSpan WaitAfter(int attempts) => TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Max(0, attempts - 1)));

    private async Task<PostOutcome> PostAsync(AnnouncementDay day, EntryAnnouncements entry, SocialPlatform platform, DateTimeOffset now, bool byOrganizer, CancellationToken cancellationToken)
    {
        if (publishers.For(platform) is not { } publisher || entry.Platforms.SingleOrDefault(p => p.Platform == platform) is not { } announcement)
        {
            return PostOutcome.Skipped;
        }

        if (!await ClaimAsync(entry.SlotId, platform, now, byOrganizer, cancellationToken))
        {
            return PostOutcome.Skipped;
        }

        var publication = new SocialPublication(
            day.Year,
            day.Day,
            entry.Part,
            entry.Title,
            Description(day, entry),
            announcement,
            cards.RenderEntry(new EntryCard(day.Year, day.Day, entry.Part, day.SlotsPerDay, entry.Title, entry.Author)));
        try
        {
            var postUrl = await publisher.PublishAsync(publication, cancellationToken);
            await RecordAsync(entry.SlotId, platform, SocialPostStatus.Posted, Uri.TryCreate(postUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? postUrl : null, null, now, cancellationToken);
            return PostOutcome.Posted;
        }
        catch (Exception exception) when (exception is SocialPublishException or HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            var error = exception is TaskCanceledException ? $"{platform} didn't answer in time." : exception.Message;
            LogFailed(exception, platform, entry.SlotId);
            await RecordAsync(entry.SlotId, platform, SocialPostStatus.Failed, null, error[..Math.Min(error.Length, 1000)], now, cancellationToken);
            return PostOutcome.Failed;
        }
    }

    /// <summary>
    /// Claims the announcement for this run: a new row, or a failed one that's due another try. Whoever claims it posts it,
    /// and everyone else skips it.
    /// </summary>
    private async Task<bool> ClaimAsync(int slotId, SocialPlatform platform, DateTimeOffset now, bool byOrganizer, CancellationToken cancellationToken)
    {
        var existing = await db.SocialPosts.AsNoTracking()
            .Where(p => p.SlotId == slotId && p.Platform == platform)
            .Select(p => new { p.Status, p.Attempts, p.At })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            var claim = new SocialPost { SlotId = slotId, Platform = platform, Status = SocialPostStatus.Posting, At = now, MarkedBy = Automation, Attempts = 1 };
            db.SocialPosts.Add(claim);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another run, or an organizer, got there first.
                db.Entry(claim).State = EntityState.Detached;
                return false;
            }
        }

        if (existing.Status is not SocialPostStatus.Failed
            || (!byOrganizer && (existing.Attempts >= options.Value.MaxAttempts || now < existing.At + WaitAfter(existing.Attempts))))
        {
            return false;
        }

        // Only the run that moves it from Failed (with the attempts it read) gets it.
        var attempts = byOrganizer ? 1 : existing.Attempts + 1;
        return await db.SocialPosts
            .Where(p => p.SlotId == slotId && p.Platform == platform && p.Status == SocialPostStatus.Failed && p.Attempts == existing.Attempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SocialPostStatus.Posting)
                .SetProperty(p => p.Attempts, attempts)
                .SetProperty(p => p.At, now)
                .SetProperty(p => p.MarkedBy, Automation), cancellationToken) == 1;
    }

    // Only while it's still this run's claim: an organizer may have marked it posted by hand in the meantime.
    private Task RecordAsync(int slotId, SocialPlatform platform, SocialPostStatus status, string? postUrl, string? error, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.SocialPosts
            .Where(p => p.SlotId == slotId && p.Platform == platform && p.Status == SocialPostStatus.Posting)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.PostUrl, postUrl)
                .SetProperty(p => p.Error, error)
                .SetProperty(p => p.At, now), cancellationToken);

    /// <summary>
    /// A post still marked as posting long after its run (the app restarted mid-post, say) may or may not have gone out, so
    /// it's marked failed for an organizer to check, and never tried again by itself.
    /// </summary>
    private Task GiveUpOnInterruptedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - InterruptedAfter;
        return db.SocialPosts
            .Where(p => p.Status == SocialPostStatus.Posting && p.At < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SocialPostStatus.Failed)
                .SetProperty(p => p.Attempts, options.Value.MaxAttempts)
                .SetProperty(p => p.Error, "Interrupted while posting. Check the account before retrying, in case it went out."), cancellationToken);
    }

    /// <summary>What a link card says under the title, like the entry page's preview.</summary>
    public static string Description(AnnouncementDay day, EntryAnnouncements entry) =>
        day.SlotsPerDay > 1
            ? $"By {entry.Author}, for C# Advent {day.Year}: December {day.Day}, post {entry.Part}."
            : $"By {entry.Author}, for C# Advent {day.Year}: December {day.Day}.";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting slot {SlotId} to {Platform} failed; it'll be tried again or shown to the organizers")]
    private partial void LogFailed(Exception exception, SocialPlatform platform, int slotId);
}
