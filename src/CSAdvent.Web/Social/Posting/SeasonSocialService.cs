using System.Globalization;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Retrospectives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Social.Posting;

/// <summary>One platform's season post: what it says, how the platform posts, and what's gone out.</summary>
/// <param name="Posts">What's gone out, newest first. "Sign-ups are open" has at most one.</param>
public sealed record SeasonPostPlatform(FormattedAnnouncement Announcement, PlatformPosting Posting, IReadOnlyList<SeasonSocialPost> Posts);

/// <param name="DaysUntil">Days until sign-ups open, while they're still to come; null once they're open or unscheduled.</param>
/// <param name="Soon">The "sign-ups open soon" post, while there's a countdown.</param>
public sealed record SeasonPosts(
    int Year,
    DateTimeOffset? OpensAt,
    bool AreOpen,
    int? DaysUntil,
    IReadOnlyList<SeasonPostPlatform> Soon,
    IReadOnlyList<SeasonPostPlatform> Open);

/// <summary>
/// Posts about a season rather than an entry (<c>/manage/social/signups</c>): "sign-ups open in 8 days", which organizers
/// post whenever they like, and "sign-ups are open", which the platforms in Automatic mode post by themselves at the
/// morning send time on the day sign-ups open, once. Also the season's retrospective, once it's published, which organizers
/// post from its page once per platform. Never for the test season. They're claimed, posted, recorded and recovered the same
/// way as entries' announcements (<see cref="SocialPostAttempts"/>), but a failure is never tried again by itself.
/// </summary>
public sealed partial class SeasonSocialService(
    AdventDbContext db,
    AdventSchedule schedule,
    SeasonAdminService seasons,
    SocialPlatformModes modes,
    SocialPublishers publishers,
    SocialPostingAlerts alerts,
    CardRenderer cards,
    IOptions<SiteOptions> site,
    IOptions<SocialAccountsOptions> accounts,
    IOptions<ReminderOptions> reminders,
    TimeProvider timeProvider,
    ILogger<SeasonSocialService> logger)
{
    public async Task<SeasonPosts?> GetAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
        {
            return null;
        }

        var (opensAt, areOpen, daysUntil) = await TimingAsync(year, cancellationToken);
        var platforms = (await modes.GetAsync(cancellationToken)).Where(p => p.Mode is not SocialPostingMode.Off).ToList();
        var posts = await db.SeasonSocialPosts.AsNoTracking().Where(p => p.SeasonYear == year).OrderByDescending(p => p.At).ToListAsync(cancellationToken);

        IReadOnlyList<SeasonPostPlatform> For(SeasonPostKind kind, Func<SocialPlatform, FormattedAnnouncement> format) =>
            [.. platforms.Select(p => new SeasonPostPlatform(format(p.Platform), p, [.. posts.Where(post => post.Kind == kind && post.Platform == p.Platform)]))];

        return new SeasonPosts(
            year,
            opensAt,
            areOpen,
            daysUntil,
            daysUntil is { } days && opensAt is { } opens
                ? For(SeasonPostKind.SignUpsSoon, platform => AnnouncementFormatter.FormatSignUpsSoon(year, LocalDate(opens), days, platform, site.Value.BaseUrl, accounts.Value))
                : [],
            For(SeasonPostKind.SignUpsOpen, platform => AnnouncementFormatter.FormatSignUpsOpen(year, platform, site.Value.BaseUrl, accounts.Value)));
    }

    /// <summary>The retrospective's post on each platform that isn't off, or null until the retrospective is published.</summary>
    public async Task<IReadOnlyList<SeasonPostPlatform>?> GetRetrospectiveAsync(int year, CancellationToken cancellationToken = default)
    {
        if (await PublishedRetrospectiveAsync(year, cancellationToken) is not { } retrospective)
        {
            return null;
        }

        var platforms = (await modes.GetAsync(cancellationToken)).Where(p => p.Mode is not SocialPostingMode.Off).ToList();
        var posts = await db.SeasonSocialPosts.AsNoTracking()
            .Where(p => p.SeasonYear == year && p.Kind == SeasonPostKind.Retrospective)
            .ToListAsync(cancellationToken);
        return [.. platforms.Select(p => new SeasonPostPlatform(
            AnnouncementFormatter.FormatRetrospective(retrospective.Title, retrospective.Stats, p.Platform, site.Value.BaseUrl, accounts.Value),
            p,
            [.. posts.Where(post => post.Platform == p.Platform)]))];
    }

    /// <summary>
    /// Posts it now on a platform that can post automatically: "soon" while sign-ups are still to come, "open" once they're
    /// open, and the retrospective once it's published (those two only once). Returns null when it can't be posted.
    /// </summary>
    public async Task<PostOutcome?> PostAsync(int year, SeasonPostKind kind, SocialPlatform platform, string postedBy, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || publishers.For(platform) is not { } publisher)
        {
            return null;
        }

        var (opensAt, areOpen, daysUntil) = await TimingAsync(year, cancellationToken);
        FormattedAnnouncement announcement;
        string description;
        byte[] card;
        if (kind is SeasonPostKind.SignUpsSoon && daysUntil is { } days && opensAt is { } opens)
        {
            announcement = AnnouncementFormatter.FormatSignUpsSoon(year, LocalDate(opens), days, platform, site.Value.BaseUrl, accounts.Value);
            description = $"Sign-ups open {LocalDate(opens).ToString("MMMM d", CultureInfo.InvariantCulture)}: two C# posts a day, December 1–25, written by the community.";
            card = cards.RenderSeason(year, null, LocalDate(opens));
        }
        else if (kind is SeasonPostKind.SignUpsOpen && areOpen)
        {
            announcement = AnnouncementFormatter.FormatSignUpsOpen(year, platform, site.Value.BaseUrl, accounts.Value);
            description = "Sign-ups are open: two C# posts a day, December 1–25, written by the community.";
            card = cards.RenderSeason(year, null);
        }
        else if (kind is SeasonPostKind.Retrospective && await PublishedRetrospectiveAsync(year, cancellationToken) is { } retrospective)
        {
            announcement = AnnouncementFormatter.FormatRetrospective(retrospective.Title, retrospective.Stats, platform, site.Value.BaseUrl, accounts.Value);
            description = RetrospectiveDraft.Summary(retrospective.Stats);
            card = cards.RenderSeason(year, retrospective.Stats.Posts);
        }
        else
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (await ClaimAsync(year, kind, platform, postedBy, now, cancellationToken) is not { } claimId)
        {
            return PostOutcome.Skipped;
        }

        var publication = new SocialPublication(year, 0, 0, kind is SeasonPostKind.Retrospective ? $"C# Advent {year}: a look back" : $"C# Advent {year}", description, announcement, card)
        {
            Key = string.Create(CultureInfo.InvariantCulture, $"csadvent-{year}-{kind}-{daysUntil}-{platform}").ToLowerInvariant(),
        };

        var attempt = await SocialPostAttempts.TryAsync(publisher, publication, cancellationToken);
        var status = SocialPostAttempts.StatusAfter(attempt, publisher.RetriesAreSafe);
        if (status is not SocialPostStatus.Posted)
        {
            LogFailed(attempt.Exception, kind, platform, year);
        }

        var recorded = await SocialPostAttempts.RecordAsync(
            db.SeasonSocialPosts.Where(p => p.Id == claimId), status, attempt.PostUrl, attempt.Error, timeProvider.GetUtcNow(), cancellationToken);

        // Nothing tries it again by itself, so an automatic post that didn't go out stops here. An organizer's own try
        // shows on their page.
        if (recorded && status is not SocialPostStatus.Posted && postedBy == SocialPostingService.Automation)
        {
            await alerts.StoppedAsync(StoppedPost.Season(platform, year, kind, status is SocialPostStatus.Unconfirmed, attempt.Error!), cancellationToken);
        }

        return status switch
        {
            SocialPostStatus.Posted => PostOutcome.Posted,
            SocialPostStatus.Unconfirmed => PostOutcome.Unconfirmed,
            _ => PostOutcome.Failed,
        };
    }

    /// <summary>Records a post an organizer made by hand. "Soon" can be marked as often as it's posted; the others once.</summary>
    public async Task<bool> MarkAsync(int year, SeasonPostKind kind, SocialPlatform platform, string? postUrl, string markedBy, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
        {
            return false;
        }

        var existing = IsOnce(kind)
            ? await db.SeasonSocialPosts.SingleOrDefaultAsync(p => p.SeasonYear == year && p.Kind == kind && p.Platform == platform, cancellationToken)
            : null;
        var post = existing ?? new SeasonSocialPost { SeasonYear = year, Kind = kind, Platform = platform, MarkedBy = markedBy };
        post.Status = SocialPostStatus.Posted;
        post.At = timeProvider.GetUtcNow();
        post.PostUrl = postUrl;
        post.MarkedBy = markedBy;
        post.Error = null;
        if (existing is null)
        {
            db.SeasonSocialPosts.Add(post);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Posted automatically at the same moment: it's marked either way.
        }

        return true;
    }

    public Task UnmarkAsync(int postId, CancellationToken cancellationToken = default) =>
        db.SeasonSocialPosts.Where(p => p.Id == postId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Posts "sign-ups are open" on every Automatic platform that hasn't had it, at the morning send time on the day the
    /// current season's sign-ups open. Returns how many went out.
    /// </summary>
    /// <param name="year">The season, instead of the current one (for the tests).</param>
    public async Task<int> RunAsync(DateTimeOffset now, int? year = null, CancellationToken cancellationToken = default)
    {
        // Any season and kind: an organizer's Post now can be interrupted too (#161).
        await GiveUpOnInterruptedAsync(cancellationToken);

        if ((year ?? await seasons.GetCurrentYearAsync(cancellationToken)) is not { } season || season == Season.TestYear
            || !schedule.IsTimeToAnnounceSignUps(
                await db.Seasons.Where(s => s.Year == season).Select(s => s.ClaimsOpenAt).SingleOrDefaultAsync(cancellationToken), now, reminders.Value.SendAt))
        {
            return 0;
        }

        var posted = await db.SeasonSocialPosts.Where(p => p.SeasonYear == season && p.Kind == SeasonPostKind.SignUpsOpen).Select(p => p.Platform).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var platform in (await modes.GetAsync(cancellationToken)).Where(p => p.IsAutomatic && !posted.Contains(p.Platform)))
        {
            if (await PostAsync(season, SeasonPostKind.SignUpsOpen, platform.Platform, SocialPostingService.Automation, cancellationToken) is PostOutcome.Posted)
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<(DateTimeOffset? OpensAt, bool AreOpen, int? DaysUntil)> TimingAsync(int year, CancellationToken cancellationToken)
    {
        var opensAt = await db.Seasons.Where(s => s.Year == year).Select(s => s.ClaimsOpenAt).SingleOrDefaultAsync(cancellationToken);
        var areOpen = schedule.AreClaimsOpen(opensAt);
        int? daysUntil = !areOpen && opensAt is { } opens && LocalDate(opens).DayNumber - schedule.Today.DayNumber is var days and >= 1 ? days : null;
        return (opensAt, areOpen, daysUntil);
    }

    /// <summary>
    /// Claims the post: a new row, except that "sign-ups are open" and the retrospective reuse a failed or unconfirmed one
    /// and are skipped once posted (or being posted).
    /// </summary>
    private async Task<int?> ClaimAsync(int year, SeasonPostKind kind, SocialPlatform platform, string postedBy, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (IsOnce(kind)
            && await db.SeasonSocialPosts.AsNoTracking()
                .Where(p => p.SeasonYear == year && p.Kind == kind && p.Platform == platform)
                .Select(p => new { p.Id })
                .SingleOrDefaultAsync(cancellationToken) is { } existing)
        {
            var retried = await db.SeasonSocialPosts
                .Where(p => p.Id == existing.Id && (p.Status == SocialPostStatus.Failed || p.Status == SocialPostStatus.Unconfirmed))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, SocialPostStatus.Posting).SetProperty(p => p.At, now).SetProperty(p => p.MarkedBy, postedBy), cancellationToken);
            return retried == 1 ? existing.Id : null;
        }

        var claim = new SeasonSocialPost { SeasonYear = year, Kind = kind, Platform = platform, Status = SocialPostStatus.Posting, At = now, MarkedBy = postedBy };
        return await SocialPostAttempts.TryClaimAsync(db, claim, cancellationToken) ? claim.Id : null;
    }

    private async Task GiveUpOnInterruptedAsync(CancellationToken cancellationToken)
    {
        foreach (var post in await SocialPostAttempts.GiveUpOnInterruptedAsync(db.SeasonSocialPosts, timeProvider.GetUtcNow(), cancellationToken))
        {
            await alerts.StoppedAsync(StoppedPost.Season(post.Platform, post.SeasonYear, post.Kind, mayHavePosted: true, SocialPostAttempts.Interrupted), cancellationToken);
        }
    }

    /// <summary>"Sign-ups are open" and the retrospective go out once per platform; "soon" as often as organizers like.</summary>
    public static bool IsOnce(SeasonPostKind kind) => kind is not SeasonPostKind.SignUpsSoon;

    private Task<Retrospective?> PublishedRetrospectiveAsync(int year, CancellationToken cancellationToken) =>
        db.Retrospectives.AsNoTracking().SingleOrDefaultAsync(r => r.SeasonYear == year && r.PublishedAt != null, cancellationToken);

    private DateOnly LocalDate(DateTimeOffset moment) => DateOnly.FromDateTime(schedule.ToLocalTime(moment).DateTime);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the {Kind} post for season {Year} to {Platform} failed; it's shown to the organizers")]
    private partial void LogFailed(Exception? exception, SeasonPostKind kind, SocialPlatform platform, int year);
}
