using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.DeadLinks;

/// <param name="Checked">Posts checked.</param>
/// <param name="FoundDead">Posts that turned dead in this run.</param>
/// <param name="FoundCopies">Archived copies found for dead posts.</param>
public sealed record LinkCheckRun(int Checked, int FoundDead, int FoundCopies)
{
    public static readonly LinkCheckRun None = new(0, 0, 0);
}

/// <summary>A post's check, for the organizers.</summary>
/// <param name="Version">The spot's row version, so a new link can't overwrite a change made in between.</param>
public sealed record LinkCheckRow(
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string? Title,
    string? AuthorName,
    string PostUrl,
    uint Version,
    LinkStatus Status,
    string? Detail,
    int Failures,
    DateTimeOffset? FailingSince,
    DateTimeOffset? DeadSince,
    DateTimeOffset? CheckedAt,
    DateTimeOffset NextCheckAt,
    string? ArchivedUrl,
    string? ArchivedBy,
    DateTimeOffset? ArchiveCheckedAt,
    string? DecidedBy)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    /// <summary>Readers who follow the post's link get its archived copy.</summary>
    public bool SendsReadersToCopy => Status == LinkStatus.Dead && ArchivedUrl is not null;
}

/// <summary>How many posts stand where, for the dead links page.</summary>
public sealed record LinkCheckCounts(int Unchecked, int Working, int Failing, int Dead, int DeadWithoutCopy, int Refused)
{
    public int Checked => Working + Failing + Dead + Refused;
}

/// <summary>What an organizer's change to a post's link or check came to.</summary>
public enum LinkActionOutcome
{
    Saved,

    /// <summary>The spot has no published post (or it's the test season's).</summary>
    NotFound,

    /// <summary>The address isn't a full web address, or it's too long.</summary>
    Invalid,

    /// <summary>The spot changed since the page was shown.</summary>
    ChangedByOthers,
}

/// <summary>
/// Checks that each published post in a finished season is still at its address (#201), slowly: <see cref="LinkCheckWorker"/>
/// runs it every 15 minutes, a few posts a run, each about once a month (<see cref="LinkCheckOptions"/>).
/// <list type="bullet">
/// <item>A post that fails <see cref="LinkCheckOptions.FailuresBeforeDead"/> checks in a row, days apart, is dead. Its archived
/// copy is looked up at the Wayback Machine, closest to its reveal, and readers who follow its link (<c>/go</c>) get that copy
/// rather than a missing page. A post that loads again is working again.</item>
/// <item>Each run leases the rows it checks, so replicas never check the same post, and spaces out each site's posts
/// (<see cref="LinkCheckOptions.HostPause"/>).</item>
/// <item>Organizers see the dead and failing posts at <c>/manage/authors/dead-links</c> and on each entry's page, and can check
/// one now, say it works or that it's gone, give it a new link, or set (or look up) its archived copy.</item>
/// </list>
/// Never the test season.
/// </summary>
public sealed partial class LinkCheckService(
    AdventDbContext db,
    AdventSchedule schedule,
    ILinkProber prober,
    IArchiveLookup archive,
    IOptions<LinkCheckOptions> options,
    ILogger<LinkCheckService> logger)
{
    /// <summary>How long a run holds a post it's checking, so another replica leaves it alone.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>The most overdue posts a run picks from, spacing out the sites among them.</summary>
    private const int Candidates = 50;

    public const string DeadLinksPage = "manage/authors/dead-links";

    private LinkCheckOptions Options => options.Value;

    /// <param name="onlyYear">Only this season (the integration tests use it so seasons don't affect each other).</param>
    public async Task<LinkCheckRun> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        // Finished seasons only: a season's posts are new until it's over, and the blurb check reads them in December anyway.
        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => year != Season.TestYear && (onlyYear is null || year == onlyYear) && now >= schedule.GetDayEndTime(year, Season.DaysInAdvent))
            .ToList();
        if (years.Count == 0)
        {
            return LinkCheckRun.None;
        }

        await TrackNewPostsAsync(years, now, cancellationToken);

        var run = LinkCheckRun.None;
        foreach (var (id, slotId, url) in await PickDueAsync(years, now, cancellationToken))
        {
            // One post that throws doesn't stop the others (#147).
            try
            {
                if (await CheckDueAsync(id, url, now, cancellationToken) is { } becameDead)
                {
                    run = run with { Checked = run.Checked + 1, FoundDead = run.FoundDead + (becameDead ? 1 : 0) };
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !MissingSchema.IsMissingTableOrColumn(exception))
            {
                db.ChangeTracker.Clear();
                LogPostFailed(exception, slotId);
            }
        }

        return run with { FoundCopies = await LookUpCopiesAsync(years, now, cancellationToken) };
    }

    /// <summary>"Check now": checks the post straight away, whatever was decided about it, and looks up a copy if it's dead.</summary>
    public async Task<LinkActionOutcome> CheckNowAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(slotId, now, cancellationToken) is not (var check, var year, var day))
        {
            return LinkActionOutcome.NotFound;
        }

        var probe = await ProbeAsync(check.PostUrl, cancellationToken);
        LinkCheckRules.After(LinkCheckState.Of(check), probe, now, Options, Random.Shared.NextDouble()).ApplyTo(check);
        check.DecidedBy = null;
        check.DecidedAt = null;
        check.LeasedUntil = null;
        await db.SaveChangesAsync(cancellationToken);

        if (check is { Status: LinkStatus.Dead, ArchivedUrl: null, ArchivedBy: null })
        {
            await LookUpAsync(check, year, day, now, cancellationToken);
        }

        return LinkActionOutcome.Saved;
    }

    /// <summary>"It works": a false alarm, such as a site that only refuses checks like ours. Stands until its link changes or someone checks it.</summary>
    public async Task<LinkActionOutcome> MarkWorkingAsync(int slotId, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(slotId, now, cancellationToken) is not (var check, _, _))
        {
            return LinkActionOutcome.NotFound;
        }

        check.Status = LinkStatus.Working;
        check.Failures = 0;
        check.FailingSince = null;
        check.DeadSince = null;
        check.Detail = $"{organizer} said it works.";
        Decide(check, organizer, now);
        await db.SaveChangesAsync(cancellationToken);
        return LinkActionOutcome.Saved;
    }

    /// <summary>
    /// "It's gone": for a page the check can't tell is gone, such as a domain that's been sold and answers with something else.
    /// Readers get its archived copy, which is looked up now if it hasn't got one.
    /// </summary>
    public async Task<LinkActionOutcome> MarkDeadAsync(int slotId, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(slotId, now, cancellationToken) is not (var check, var year, var day))
        {
            return LinkActionOutcome.NotFound;
        }

        check.Status = LinkStatus.Dead;
        check.DeadSince ??= now;
        check.Detail = $"{organizer} marked it as gone.";
        Decide(check, organizer, now);
        await db.SaveChangesAsync(cancellationToken);

        if (check is { ArchivedUrl: null, ArchivedBy: null })
        {
            await LookUpAsync(check, year, day, now, cancellationToken);
        }

        return LinkActionOutcome.Saved;
    }

    /// <summary>
    /// The archived copy readers get while the post is dead, from an organizer: any web address (the Wayback Machine's, or
    /// archive.today's, say). Empty clears it. Either way it's theirs, so runs never look one up in its place.
    /// </summary>
    public async Task<LinkActionOutcome> SetArchivedCopyAsync(int slotId, string? url, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var copy = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (copy is not null && !IsWebAddress(copy, maxLength: 1000))
        {
            return LinkActionOutcome.Invalid;
        }

        if (await LoadAsync(slotId, now, cancellationToken) is not (var check, _, _))
        {
            return LinkActionOutcome.NotFound;
        }

        check.ArchivedUrl = copy;
        check.ArchivedBy = organizer;
        check.ArchiveCheckedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return LinkActionOutcome.Saved;
    }

    /// <summary>"Look it up": asks the Wayback Machine now, replacing any copy, and hands the copy back to the automatic lookups.</summary>
    public async Task<LinkActionOutcome> LookUpCopyAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(slotId, now, cancellationToken) is not (var check, var year, var day))
        {
            return LinkActionOutcome.NotFound;
        }

        check.ArchivedBy = null;
        check.ArchivedUrl = null;
        await LookUpAsync(check, year, day, now, cancellationToken);
        return LinkActionOutcome.Saved;
    }

    /// <summary>
    /// A new link for the post, for an author who moved it: the spot's address changes everywhere at once (every link we've
    /// published goes through <c>/go</c>), guarded by the spot's row version, and the new address is checked from scratch.
    /// </summary>
    public async Task<LinkActionOutcome> ReplaceAsync(int slotId, uint version, string? url, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var postUrl = url?.Trim();
        if (postUrl is null || !IsWebAddress(postUrl, maxLength: 500))
        {
            return LinkActionOutcome.Invalid;
        }

        var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == slotId && s.SeasonYear != Season.TestYear && s.PostUrl != null && s.AuthorName != null, cancellationToken);
        if (slot is null)
        {
            return LinkActionOutcome.NotFound;
        }

        // Compare against the version the organizer saw, not the one just loaded.
        db.Entry(slot).Property(s => s.Version).OriginalValue = version;
        slot.PostUrl = postUrl;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return LinkActionOutcome.ChangedByOthers;
        }

        var detail = $"{organizer} gave it a new link.";
        await db.LinkChecks
            .Where(c => c.SlotId == slotId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.PostUrl, postUrl)
                    .SetProperty(c => c.Status, LinkStatus.Unchecked)
                    .SetProperty(c => c.Detail, detail)
                    .SetProperty(c => c.Failures, 0)
                    .SetProperty(c => c.FailingSince, (DateTimeOffset?)null)
                    .SetProperty(c => c.DeadSince, (DateTimeOffset?)null)
                    .SetProperty(c => c.CheckedAt, (DateTimeOffset?)null)
                    .SetProperty(c => c.NextCheckAt, now)
                    .SetProperty(c => c.LeasedUntil, (DateTimeOffset?)null)
                    .SetProperty(c => c.ArchivedUrl, (string?)null)
                    .SetProperty(c => c.ArchivedBy, (string?)null)
                    .SetProperty(c => c.ArchiveCheckedAt, (DateTimeOffset?)null)
                    .SetProperty(c => c.DecidedBy, (string?)null)
                    .SetProperty(c => c.DecidedAt, (DateTimeOffset?)null),
                cancellationToken);
        return LinkActionOutcome.Saved;
    }

    /// <summary>The dead, failing and turned-away posts, newest season first, with how many posts stand where.</summary>
    public async Task<(IReadOnlyList<LinkCheckRow> Rows, LinkCheckCounts Counts)> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Rows(db.LinkChecks.Where(c => c.Status == LinkStatus.Dead || c.Status == LinkStatus.Failing || c.Status == LinkStatus.Refused))
            .ToListAsync(cancellationToken);
        var counts = await Current(db.LinkChecks)
            .GroupBy(c => new { c.Status, HasCopy = c.ArchivedUrl != null })
            .Select(g => new { g.Key.Status, g.Key.HasCopy, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(LinkStatus status) => counts.Where(c => c.Status == status).Sum(c => c.Count);
        return (rows, new LinkCheckCounts(
            Count(LinkStatus.Unchecked),
            Count(LinkStatus.Working),
            Count(LinkStatus.Failing),
            Count(LinkStatus.Dead),
            counts.Where(c => c is { Status: LinkStatus.Dead, HasCopy: false }).Sum(c => c.Count),
            Count(LinkStatus.Refused)));
    }

    /// <summary>
    /// The post's check, for its entry page, or null if it hasn't one for its current address. The entry pages, the entries list
    /// and Authors are busy in December, so these three take a table the deploy's migrations haven't added yet as no checks (#225).
    /// </summary>
    public Task<LinkCheckRow?> GetAsync(int slotId, CancellationToken cancellationToken = default) =>
        BeforeMigrations(() => Rows(db.LinkChecks.Where(c => c.SlotId == slotId)).SingleOrDefaultAsync(cancellationToken), null, cancellationToken);

    /// <summary>The season's dead posts, for the entries list.</summary>
    public Task<HashSet<int>> GetDeadAsync(int year, CancellationToken cancellationToken = default) =>
        BeforeMigrations(
            async () => (HashSet<int>)[.. await Current(db.LinkChecks)
                .Where(c => c.Slot.SeasonYear == year && c.Status == LinkStatus.Dead)
                .Select(c => c.SlotId)
                .ToListAsync(cancellationToken)],
            [],
            cancellationToken);

    public Task<int> CountDeadAsync(CancellationToken cancellationToken = default) =>
        BeforeMigrations(() => Current(db.LinkChecks).CountAsync(c => c.Status == LinkStatus.Dead, cancellationToken), 0, cancellationToken);

    private static async Task<T> BeforeMigrations<T>(Func<Task<T>> query, T none, CancellationToken cancellationToken)
    {
        try
        {
            return await query();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
        {
            return none;
        }
    }

    public static bool IsWebAddress(string url, int maxLength) =>
        url.Length <= maxLength
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.Host.Length > 0;

    /// <summary>Checks about the spots' current addresses (a changed address is checked again from scratch) in real seasons.</summary>
    private static IQueryable<LinkCheck> Current(IQueryable<LinkCheck> checks) =>
        checks.Where(c => c.PostUrl == c.Slot.PostUrl && c.Slot.AuthorName != null && c.Slot.SeasonYear != Season.TestYear);

    private static IQueryable<LinkCheckRow> Rows(IQueryable<LinkCheck> checks) =>
        Current(checks)
            .AsNoTracking()
            .OrderByDescending(c => c.Slot.SeasonYear).ThenBy(c => c.Slot.Day).ThenBy(c => c.Slot.Part)
            .Select(c => new LinkCheckRow(
                c.SlotId,
                c.Slot.SeasonYear,
                c.Slot.Day,
                c.Slot.Part,
                c.Slot.Season.SlotsPerDay,
                c.Slot.PostTitle,
                c.Slot.AuthorName,
                c.PostUrl,
                c.Slot.Version,
                c.Status,
                c.Detail,
                c.Failures,
                c.FailingSince,
                c.DeadSince,
                c.CheckedAt,
                c.NextCheckAt,
                c.ArchivedUrl,
                c.ArchivedBy,
                c.ArchiveCheckedAt,
                c.DecidedBy));

    /// <summary>
    /// Gives every published post in the seasons a check, due now, and starts again for any whose address changed since. Rows
    /// are added with <c>ON CONFLICT DO NOTHING</c>, so two replicas adding the same one at once is fine.
    /// </summary>
    private async Task TrackNewPostsAsync(List<int> years, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => years.Contains(s.SeasonYear) && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, PostUrl = s.PostUrl! })
            .ToListAsync(cancellationToken);
        var tracked = await db.LinkChecks
            .AsNoTracking()
            .Where(c => years.Contains(c.Slot.SeasonYear))
            .Select(c => new { c.SlotId, c.PostUrl })
            .ToDictionaryAsync(c => c.SlotId, c => c.PostUrl, cancellationToken);

        var missing = posts.Where(p => !tracked.ContainsKey(p.Id)).ToList();
        if (missing.Count > 0)
        {
            var slotIds = missing.Select(p => p.Id).ToArray();
            var urls = missing.Select(p => p.PostUrl).ToArray();
            var initial = nameof(LinkStatus.Unchecked);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO link_checks (slot_id, post_url, status, failures, next_check_at)
                SELECT t.slot_id, t.post_url, {initial}, 0, {now} FROM unnest({slotIds}, {urls}) AS t(slot_id, post_url)
                ON CONFLICT (slot_id) DO NOTHING
                """,
                cancellationToken);
        }

        foreach (var post in posts.Where(p => tracked.TryGetValue(p.Id, out var url) && url != p.PostUrl))
        {
            var oldUrl = tracked[post.Id];
            await db.LinkChecks
                .Where(c => c.SlotId == post.Id && c.PostUrl == oldUrl)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.PostUrl, post.PostUrl)
                        .SetProperty(c => c.Status, LinkStatus.Unchecked)
                        .SetProperty(c => c.Detail, (string?)null)
                        .SetProperty(c => c.Failures, 0)
                        .SetProperty(c => c.FailingSince, (DateTimeOffset?)null)
                        .SetProperty(c => c.DeadSince, (DateTimeOffset?)null)
                        .SetProperty(c => c.CheckedAt, (DateTimeOffset?)null)
                        .SetProperty(c => c.NextCheckAt, now)
                        .SetProperty(c => c.LeasedUntil, (DateTimeOffset?)null)
                        .SetProperty(c => c.ArchivedUrl, (string?)null)
                        .SetProperty(c => c.ArchivedBy, (string?)null)
                        .SetProperty(c => c.ArchiveCheckedAt, (DateTimeOffset?)null)
                        .SetProperty(c => c.DecidedBy, (string?)null)
                        .SetProperty(c => c.DecidedAt, (DateTimeOffset?)null),
                    cancellationToken);
        }
    }

    /// <summary>
    /// The posts this run checks: the most overdue first, at most <see cref="LinkCheckOptions.PostsPerRun"/>, and at most one
    /// per site, skipping sites checked in the last <see cref="LinkCheckOptions.HostPause"/>.
    /// </summary>
    private async Task<List<(int Id, int SlotId, string Url)>> PickDueAsync(List<int> years, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await Current(db.LinkChecks)
            .AsNoTracking()
            .Where(c => years.Contains(c.Slot.SeasonYear) && c.Slot.PostTitle != null && c.DecidedBy == null && c.NextCheckAt <= now && (c.LeasedUntil == null || c.LeasedUntil <= now))
            .OrderBy(c => c.NextCheckAt).ThenBy(c => c.Id)
            .Take(Candidates)
            .Select(c => new { c.Id, c.SlotId, c.PostUrl })
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return [];
        }

        var since = now - Options.HostPause;
        var recent = await db.LinkChecks
            .AsNoTracking()
            .Where(c => years.Contains(c.Slot.SeasonYear) && c.CheckedAt > since && c.CheckedAt <= now)
            .Select(c => c.PostUrl)
            .ToListAsync(cancellationToken);
        return Pick(due.Select(c => (c.Id, c.SlotId, c.PostUrl)), recent, Options.PostsPerRun);
    }

    /// <summary>Up to <paramref name="count"/> of the posts, in order, one per site, leaving out the sites checked recently.</summary>
    public static List<(int Id, int SlotId, string Url)> Pick(IEnumerable<(int Id, int SlotId, string Url)> due, IEnumerable<string> recentlyChecked, int count)
    {
        var hosts = recentlyChecked.Select(LinkProbes.HostOf).ToHashSet(StringComparer.Ordinal);
        var picked = new List<(int, int, string)>();
        foreach (var post in due)
        {
            if (picked.Count == count)
            {
                break;
            }

            if (hosts.Add(LinkProbes.HostOf(post.Url)))
            {
                picked.Add(post);
            }
        }

        return picked;
    }

    /// <summary>
    /// Checks one due post, holding it with a lease so another replica leaves it alone. Null if another replica or an organizer
    /// got there first (or its address changed meanwhile); otherwise whether it turned dead.
    /// </summary>
    private async Task<bool?> CheckDueAsync(int id, string url, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var claimed = await db.LinkChecks
            .Where(c => c.Id == id && c.PostUrl == url && c.DecidedBy == null && c.NextCheckAt <= now && (c.LeasedUntil == null || c.LeasedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeasedUntil, now + Lease), cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var probe = await ProbeAsync(url, cancellationToken);
        var before = LinkCheckState.Of(await db.LinkChecks.AsNoTracking().SingleAsync(c => c.Id == id, cancellationToken));
        var after = LinkCheckRules.After(before, probe, now, Options, Random.Shared.NextDouble());

        // Not over an organizer's decision, or a new address, made while it was being checked.
        var saved = await db.LinkChecks
            .Where(c => c.Id == id && c.PostUrl == url && c.DecidedBy == null)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.Status, after.Status)
                    .SetProperty(c => c.Failures, after.Failures)
                    .SetProperty(c => c.FailingSince, after.FailingSince)
                    .SetProperty(c => c.DeadSince, after.DeadSince)
                    .SetProperty(c => c.Detail, after.Detail)
                    .SetProperty(c => c.CheckedAt, after.CheckedAt)
                    .SetProperty(c => c.NextCheckAt, after.NextCheckAt)
                    .SetProperty(c => c.LeasedUntil, (DateTimeOffset?)null),
                cancellationToken);
        if (saved == 0)
        {
            return null;
        }

        if (after.Status == LinkStatus.Dead && before.Status != LinkStatus.Dead)
        {
            LogDead(id, LinkProbes.HostOf(url));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Looks up archived copies for dead posts without one, at most <see cref="LinkCheckOptions.ArchiveLookupsPerRun"/> a run,
    /// each claimed first so replicas never ask about the same post. A post the Wayback Machine has no copy of is asked about
    /// again after <see cref="LinkCheckOptions.ArchiveRecheckAfter"/>.
    /// </summary>
    private async Task<int> LookUpCopiesAsync(List<int> years, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Options.ArchiveLookupsPerRun == 0)
        {
            return 0;
        }

        var askedBefore = now - Options.ArchiveRecheckAfter;
        var dead = await Current(db.LinkChecks)
            .AsNoTracking()
            .Where(c => years.Contains(c.Slot.SeasonYear) && c.Status == LinkStatus.Dead && c.ArchivedUrl == null && c.ArchivedBy == null
                && (c.ArchiveCheckedAt == null || c.ArchiveCheckedAt <= askedBefore))
            .OrderBy(c => c.ArchiveCheckedAt != null).ThenBy(c => c.ArchiveCheckedAt).ThenBy(c => c.Id)
            .Take(Options.ArchiveLookupsPerRun)
            .Select(c => new { c.Id, c.SlotId, c.PostUrl, c.Slot.SeasonYear, c.Slot.Day })
            .ToListAsync(cancellationToken);

        var found = 0;
        foreach (var post in dead)
        {
            try
            {
                var claimed = await db.LinkChecks
                    .Where(c => c.Id == post.Id && c.ArchivedUrl == null && c.ArchivedBy == null && (c.ArchiveCheckedAt == null || c.ArchiveCheckedAt <= askedBefore))
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchiveCheckedAt, now), cancellationToken);
                if (claimed == 0)
                {
                    continue;
                }

                var answer = await archive.FindAsync(post.PostUrl, schedule.GetDate(post.SeasonYear, post.Day), cancellationToken);
                var checkedAt = LinkCheckRules.ArchiveCheckedAt(answer, now, Options);
                await db.LinkChecks
                    .Where(c => c.Id == post.Id && c.PostUrl == post.PostUrl && c.ArchivedUrl == null && c.ArchivedBy == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ArchivedUrl, answer.CopyUrl).SetProperty(c => c.ArchiveCheckedAt, checkedAt), cancellationToken);
                found += answer.CopyUrl is null ? 0 : 1;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !MissingSchema.IsMissingTableOrColumn(exception))
            {
                LogPostFailed(exception, post.SlotId);
            }
        }

        return found;
    }

    private async Task LookUpAsync(LinkCheck check, int year, int day, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var answer = await archive.FindAsync(check.PostUrl, schedule.GetDate(year, day), cancellationToken);
        check.ArchivedUrl = answer.CopyUrl;
        check.ArchiveCheckedAt = LinkCheckRules.ArchiveCheckedAt(answer, now, Options);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<LinkProbe> ProbeAsync(string url, CancellationToken cancellationToken) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? prober.ProbeAsync(uri, cancellationToken)
            : Task.FromResult(LinkProbe.Failed("It isn't a web address."));

    /// <summary>
    /// The spot's check, tracked for an organizer's change: added if it hasn't one, and started again if its address changed.
    /// Null for a spot without a published post, or the test season's.
    /// </summary>
    private async Task<(LinkCheck Check, int Year, int Day)?> LoadAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var slot = await db.Slots
            .AsNoTracking()
            .Where(s => s.Id == slotId && s.SeasonYear != Season.TestYear && s.AuthorName != null && s.PostUrl != null)
            .Select(s => new { s.Id, PostUrl = s.PostUrl!, s.SeasonYear, s.Day })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is null)
        {
            return null;
        }

        var initial = nameof(LinkStatus.Unchecked);
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO link_checks (slot_id, post_url, status, failures, next_check_at) VALUES ({slot.Id}, {slot.PostUrl}, {initial}, 0, {now}) ON CONFLICT (slot_id) DO NOTHING",
            cancellationToken);
        var check = await db.LinkChecks.SingleAsync(c => c.SlotId == slotId, cancellationToken);
        if (check.PostUrl != slot.PostUrl)
        {
            new LinkCheckState(LinkStatus.Unchecked, 0, null, null, null, null, now).ApplyTo(check);
            check.PostUrl = slot.PostUrl;
            check.ArchivedUrl = null;
            check.ArchivedBy = null;
            check.ArchiveCheckedAt = null;
            check.DecidedBy = null;
            check.DecidedAt = null;
        }

        return (check, slot.SeasonYear, slot.Day);
    }

    private static void Decide(LinkCheck check, string organizer, DateTimeOffset now)
    {
        check.DecidedBy = organizer;
        check.DecidedAt = now;
        check.CheckedAt = now;
        check.LeasedUntil = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A post on {Host} is dead (link check {Id}).")]
    private partial void LogDead(int id, string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "The dead-link check of slot {SlotId} failed; the others carried on")]
    private partial void LogPostFailed(Exception exception, int slotId);
}
