using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Snapshots;

/// <param name="Asked">Snapshots asked for that the Wayback Machine started saving.</param>
/// <param name="Saved">Snapshots the Wayback Machine said it saved.</param>
/// <param name="GaveUp">Snapshots given up on, after <see cref="SnapshotOptions.MaxAttempts"/> tries.</param>
public sealed record SnapshotRun(int Asked, int Saved, int GaveUp)
{
    public static readonly SnapshotRun None = new(0, 0, 0);
}

/// <summary>One of a post's snapshots, for its entry page.</summary>
public sealed record SnapshotRow(
    SnapshotMoment Moment,
    SnapshotStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? RequestedAt,
    string? SnapshotUrl,
    DateTimeOffset? SavedAt,
    string? Detail);

/// <summary>
/// Asks the Internet Archive's Wayback Machine to save each post (#261), so readers have a copy to go to if it's ever gone:
/// <see cref="SnapshotWorker"/> runs it every 15 minutes.
/// <list type="bullet">
/// <item>Each post is saved as it went live (<see cref="SnapshotMoment.Live"/>) and after its season
/// (<see cref="SnapshotMoment.AfterSeason"/>), which past seasons' posts get too, and again at a new address
/// (<see cref="SnapshotRules"/>).</item>
/// <item>Each run asks about the captures in progress, then asks for a few more (<see cref="SnapshotOptions.CapturesPerRun"/>),
/// live ones first. Each is leased first, so replicas never ask about the same one.</item>
/// <item>The dead-link check sends readers to a gone post's earliest snapshot (<see cref="FindSavedAsync"/>), and organizers
/// see each post's on its entry page.</item>
/// </list>
/// Never the test season.
/// </summary>
public sealed partial class SnapshotService(
    AdventDbContext db,
    AdventSchedule schedule,
    ISnapshotSaver saver,
    IOptions<SnapshotOptions> options,
    ILogger<SnapshotService> logger)
{
    /// <summary>How long a run holds a snapshot it's asking about, so another replica leaves it alone.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>The most captures in progress asked about in one run.</summary>
    private const int StatusChecksPerRun = 20;

    private SnapshotOptions Options => options.Value;

    /// <param name="onlyYear">Only this season (the integration tests use it so seasons don't affect each other).</param>
    public async Task<SnapshotRun> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        if (!Options.IsEnabled)
        {
            return SnapshotRun.None;
        }

        // Seasons with a post revealed: nothing about a post is shared before its reveal (#5), not even with the archive.
        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => year != Season.TestYear && (onlyYear is null || year == onlyYear) && schedule.IsRevealed(year, 1, now))
            .ToList();
        if (years.Count == 0)
        {
            return SnapshotRun.None;
        }

        await TrackDueAsync(years, now, cancellationToken);

        // The captures already going first, since the ones asked for now won't be done until the next run.
        var run = SnapshotRun.None;
        foreach (var snapshot in await PickAsync(years, SnapshotStatus.Capturing, StatusChecksPerRun, now, cancellationToken))
        {
            run = Add(run, await AdvanceAsync(snapshot, now, cancellationToken));
        }

        foreach (var snapshot in await PickAsync(years, SnapshotStatus.Due, Options.CapturesPerRun, now, cancellationToken))
        {
            run = Add(run, await AdvanceAsync(snapshot, now, cancellationToken));
        }

        return run;
    }

    /// <summary>
    /// The earliest snapshot saved of the post at this address, for the dead-link check to send readers to once it's gone.
    /// Before the deploy's migrations add the table, there are none (#225).
    /// </summary>
    public static async Task<string?> FindSavedAsync(AdventDbContext db, int slotId, string postUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await db.PostSnapshots
                .AsNoTracking()
                .Where(p => p.SlotId == slotId && p.PostUrl == postUrl && p.Status == SnapshotStatus.Saved && p.SnapshotUrl != null)
                .OrderBy(p => p.SavedAt).ThenBy(p => p.Id)
                .Select(p => p.SnapshotUrl)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
        {
            return null;
        }
    }

    /// <summary>The post's snapshots at its current address, for its entry page. None before the deploy's migrations (#225).</summary>
    public async Task<IReadOnlyList<SnapshotRow>> ListAsync(int slotId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.PostSnapshots
                .AsNoTracking()
                .Where(p => p.SlotId == slotId && p.PostUrl == p.Slot.PostUrl && p.Slot.SeasonYear != Season.TestYear)
                .OrderBy(p => p.Moment == SnapshotMoment.AfterSeason).ThenBy(p => p.Id)
                .Select(p => new SnapshotRow(p.Moment, p.Status, p.Attempts, p.NextAttemptAt, p.RequestedAt, p.SnapshotUrl, p.SavedAt, p.Detail))
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
        {
            return [];
        }
    }

    /// <summary>
    /// Adds a snapshot, due now, for each moment a published post is due one at its current address and hasn't got
    /// (<see cref="SnapshotRules"/>). Rows are added with <c>ON CONFLICT DO NOTHING</c>, so two replicas adding the same one at
    /// once is fine.
    /// </summary>
    private async Task TrackDueAsync(List<int> years, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => years.Contains(s.SeasonYear) && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.SeasonYear, s.Day, PostUrl = s.PostUrl! })
            .ToListAsync(cancellationToken);
        var seen = (await db.BlurbChecks
            .AsNoTracking()
            .Where(b => years.Contains(b.Slot.SeasonYear) && b.PostUrl == b.Slot.PostUrl && (b.Status == BlurbStatus.Passed || b.Status == BlurbStatus.Waived))
            .Select(b => b.SlotId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var links = await db.LinkChecks
            .AsNoTracking()
            .Where(c => years.Contains(c.Slot.SeasonYear) && c.PostUrl == c.Slot.PostUrl)
            .Select(c => new { c.SlotId, c.Status })
            .ToDictionaryAsync(c => c.SlotId, c => c.Status, cancellationToken);
        var recorded = (await db.PostSnapshots
            .AsNoTracking()
            .Where(p => years.Contains(p.Slot.SeasonYear))
            .Select(p => new { p.SlotId, p.Moment, p.PostUrl })
            .ToListAsync(cancellationToken)).Select(p => (p.SlotId, p.Moment, p.PostUrl)).ToHashSet();

        var due = new List<(int SlotId, SnapshotMoment Moment, string PostUrl)>();
        foreach (var post in posts)
        {
            if (SnapshotRules.IsLiveDue(schedule.GetRevealTime(post.SeasonYear, post.Day), schedule.GetDayEndTime(post.SeasonYear, post.Day), seen.Contains(post.Id), now, Options.LiveWithin))
            {
                due.Add((post.Id, SnapshotMoment.Live, post.PostUrl));
            }

            if (SnapshotRules.IsAfterSeasonDue(schedule.GetDayEndTime(post.SeasonYear, Season.DaysInAdvent), links.TryGetValue(post.Id, out var link) ? link : null, now))
            {
                due.Add((post.Id, SnapshotMoment.AfterSeason, post.PostUrl));
            }
        }

        var missing = due.Where(d => !recorded.Contains(d)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var slotIds = missing.Select(d => d.SlotId).ToArray();
        var moments = missing.Select(d => d.Moment.ToString()).ToArray();
        var urls = missing.Select(d => d.PostUrl).ToArray();
        var initial = nameof(SnapshotStatus.Due);
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO post_snapshots (slot_id, moment, post_url, status, attempts, next_attempt_at)
            SELECT t.slot_id, t.moment, t.post_url, {initial}, 0, {now} FROM unnest({slotIds}, {moments}, {urls}) AS t(slot_id, moment, post_url)
            ON CONFLICT (slot_id, moment, post_url) DO NOTHING
            """,
            cancellationToken);
    }

    /// <summary>
    /// Snapshots in the given state that are ready, for the spots' current addresses: live ones first, then the longest
    /// waiting. Due ones only from their <see cref="PostSnapshot.NextAttemptAt"/>.
    /// </summary>
    private Task<List<int>> PickAsync(List<int> years, SnapshotStatus status, int count, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.PostSnapshots
            .AsNoTracking()
            .Where(p => years.Contains(p.Slot.SeasonYear) && p.PostUrl == p.Slot.PostUrl && p.Slot.AuthorName != null && p.Status == status
                && (status != SnapshotStatus.Due || p.NextAttemptAt <= now)
                && (p.LeasedUntil == null || p.LeasedUntil <= now))
            .OrderBy(p => p.Moment == SnapshotMoment.AfterSeason).ThenBy(p => p.NextAttemptAt).ThenBy(p => p.Id)
            .Take(count)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Asks for the snapshot, or about it, holding it with a lease so another replica leaves it alone, and saves what the
    /// Wayback Machine said. Null if another replica got there first, or it threw (logged; the next run tries again).
    /// </summary>
    private async Task<SnapshotState?> AdvanceAsync(int id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var claimed = await db.PostSnapshots
                .Where(p => p.Id == id && (p.Status == SnapshotStatus.Due || p.Status == SnapshotStatus.Capturing) && (p.LeasedUntil == null || p.LeasedUntil <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LeasedUntil, now + Lease), cancellationToken);
            if (claimed == 0)
            {
                return null;
            }

            var snapshot = await db.PostSnapshots.AsNoTracking().SingleAsync(p => p.Id == id, cancellationToken);
            var before = SnapshotState.Of(snapshot);
            var answer = before is { Status: SnapshotStatus.Capturing, JobId: { } jobId, RequestedAt: { } requestedAt }
                ? await saver.StatusAsync(new CaptureJob(jobId, snapshot.PostUrl, requestedAt), cancellationToken)
                : await saver.StartAsync(snapshot.PostUrl, cancellationToken);
            var after = SnapshotRules.After(before, answer, now, Options);

            await db.PostSnapshots
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(p => p.Status, after.Status)
                        .SetProperty(p => p.Attempts, after.Attempts)
                        .SetProperty(p => p.NextAttemptAt, after.NextAttemptAt)
                        .SetProperty(p => p.JobId, after.JobId)
                        .SetProperty(p => p.RequestedAt, after.RequestedAt)
                        .SetProperty(p => p.SnapshotUrl, after.SnapshotUrl)
                        .SetProperty(p => p.SavedAt, after.SavedAt)
                        .SetProperty(p => p.Detail, after.Detail)
                        .SetProperty(p => p.LeasedUntil, (DateTimeOffset?)null),
                    cancellationToken);

            if (after.Status == SnapshotStatus.Failed)
            {
                LogGaveUp(snapshot.SlotId, snapshot.Moment, after.Attempts);
            }

            return before.Status == after.Status ? null : after;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !MissingSchema.IsMissingTableOrColumn(exception))
        {
            LogSnapshotFailed(exception, id);
            return null;
        }
    }

    private static SnapshotRun Add(SnapshotRun run, SnapshotState? after) => after?.Status switch
    {
        SnapshotStatus.Capturing => run with { Asked = run.Asked + 1 },
        SnapshotStatus.Saved => run with { Saved = run.Saved + 1 },
        SnapshotStatus.Failed => run with { GaveUp = run.GaveUp + 1 },
        _ => run,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Gave up on the {Moment} snapshot of slot {SlotId} after {Attempts} tries.")]
    private partial void LogGaveUp(int slotId, SnapshotMoment moment, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Snapshot {Id} failed; the others carried on")]
    private partial void LogSnapshotFailed(Exception exception, int id);
}
