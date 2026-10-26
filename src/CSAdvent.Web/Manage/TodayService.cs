using System.Globalization;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Manage;

/// <summary>Where an entry's announcement stands on one platform, for the Today panel and the morning digest (#171).</summary>
public enum TodayPostingStatus
{
    /// <summary>Posted, by an organizer or automatically.</summary>
    Posted,

    /// <summary>Automatic posting is posting it right now.</summary>
    Posting,

    /// <summary>Automatic posting failed, and is trying again or waiting for an organizer.</summary>
    Failed,

    /// <summary>It may have gone out, but the platform never said (#154): an organizer looks at the account.</summary>
    Unconfirmed,

    /// <summary>Automatic: it goes out at its time on its day.</summary>
    Scheduled,

    /// <summary>Automatic: its time has come, so it goes out within a few minutes.</summary>
    Due,

    /// <summary>Automatic, but it waits until its blurb check passes (or an organizer lets it through).</summary>
    HeldForBlurb,

    /// <summary>By hand: it's revealed, and nobody has marked it posted yet.</summary>
    ToPost,

    /// <summary>By hand, once it's revealed.</summary>
    ByHandLater,
}

/// <summary>One platform's announcement of a spot's post.</summary>
/// <param name="IsAutomatic">The platform posts it by itself (never in the test season).</param>
/// <param name="MarkedBy">Who marked it posted: an organizer's GitHub login, or <see cref="SocialPostingService.Automation"/>.</param>
/// <param name="Error">Why it failed, or why it may not have gone out.</param>
public sealed record TodayPosting(SocialPlatform Platform, bool IsAutomatic, TodayPostingStatus Status, string? MarkedBy = null, string? Error = null);

/// <summary>A spot on a day of the Today panel: the spot, its blurb check, and its announcement on each platform that isn't off.</summary>
/// <param name="Blurb">Its post's blurb check, when the season has the rule and posts are being checked.</param>
/// <param name="AnnouncedAt">When its post is announced (<see cref="SocialPostingOptions.PostAt"/>): 10 AM for post 1, 2 PM for post 2.</param>
/// <param name="Posting">Only for a spot whose post has its title and link; nothing is announced without them.</param>
public sealed record TodaySpot(EntryListItem Entry, BlurbState? Blurb, bool NeedsAttention, DateTimeOffset AnnouncedAt, IReadOnlyList<TodayPosting> Posting);

/// <param name="IsToday">Today, rather than tomorrow, in the season's own calendar (the test season's follows its rehearsal).</param>
/// <param name="IsRevealed">Its posts are revealed. Tomorrow's never are: the morning digest leaves out their titles and links.</param>
public sealed record TodayDay(int Day, bool IsToday, bool IsRevealed, IReadOnlyList<TodaySpot> Spots);

/// <summary>An entry's announcement that failed, or may not have gone out, on one platform.</summary>
public sealed record TodayFailedPost(int SlotId, int Day, int Part, string Title, SocialPlatform Platform, bool Unconfirmed, string? Error);

/// <summary>A season with an Advent day today or tomorrow.</summary>
/// <param name="Backups">Backups asked about its spots who still have time to answer, and backups standing by.</param>
/// <param name="FailedPosts">Yesterday's and today's announcements that failed or may not have gone out, until they're sorted out.</param>
public sealed record TodaySeason(
    int Year,
    int SlotsPerDay,
    IReadOnlyList<TodayDay> Days,
    IReadOnlyList<ActiveBackupOffer> Backups,
    IReadOnlyList<TodayFailedPost> FailedPosts)
{
    /// <summary>The test season: its days follow the rehearsal, and nothing is ever posted automatically.</summary>
    public bool IsTest => Year == Season.TestYear;

    public TodayDay? Today => Days.FirstOrDefault(d => d.IsToday);

    public TodayDay? Tomorrow => Days.FirstOrDefault(d => !d.IsToday);

    /// <summary>"December 6", or "December 6, post 2" in a season with two posts a day.</summary>
    public string Spot(int day, int part) =>
        SlotsPerDay > 1 ? string.Create(CultureInfo.InvariantCulture, $"December {day}, post {part}") : string.Create(CultureInfo.InvariantCulture, $"December {day}");
}

/// <summary>What's waiting for an organizer, on every organizer page's navigation, plus the emails waiting to go.</summary>
public sealed record TodayQueues(int Requests, int PostClaims, int LinkChanges, int EmailsWaiting, int FailedEmails);

/// <summary>An organizer's day in December (#171): the Today panel on <c>/manage</c>, and the morning digest.</summary>
/// <param name="Date">Today, in the reveal time zone.</param>
/// <param name="TimeZone">The reveal time zone, which every time is shown in.</param>
/// <param name="TimeZoneName">What the reveal time zone is called (<see cref="AdventSchedule.TimeZoneName"/>, such as "Eastern").</param>
/// <param name="Seasons">The seasons with an Advent day today or tomorrow.</param>
/// <param name="FailedEmails">The emails the queue gave up on, newest first.</param>
public sealed record TodayReport(
    DateTimeOffset At,
    DateOnly Date,
    TimeZoneInfo TimeZone,
    string TimeZoneName,
    IReadOnlyList<TodaySeason> Seasons,
    IReadOnlyList<FailedEmail> FailedEmails,
    TodayQueues Queues)
{
    /// <summary>"10:00 PM Eastern on December 6", as <see cref="AdventSchedule.Describe(DateTimeOffset)"/> and the other emails give a time.</summary>
    public string Describe(DateTimeOffset moment) => AdventSchedule.Describe(moment, TimeZone, TimeZoneName);
}

/// <summary>
/// Puts an organizer's day together (#171) from the services the organizer pages already use: the entries list, the social
/// page's announcements, the blurb checks, the backup offers, the email queue and the navigation's counts. Days follow the
/// season's own calendar (<see cref="AdventSchedule"/>), so the test season's rehearsal has a today and a tomorrow too.
/// Everything here is for organizers: the morning digest leaves out what isn't public before a post's reveal.
/// </summary>
public sealed class TodayService(
    AdventDbContext db,
    AdventSchedule schedule,
    EntryAdminService entries,
    SocialService social,
    SocialPlatformModes modes,
    BlurbQueries blurbs,
    BackupListService backups,
    EmailOutbox outbox,
    OrganizerAttention attention,
    IOptions<SocialPostingOptions> posting)
{
    /// <summary>The day, as <paramref name="now"/> sees it.</summary>
    /// <param name="year">
    /// Only this season (the test season's is <c>/manage?year=9000</c>). Without one, the real seasons with an Advent day today or
    /// tomorrow: the organizer pages keep the test season to its own pages.
    /// </param>
    public async Task<TodayReport> GetAsync(DateTimeOffset now, int? year = null, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(schedule.ToLocalTime(now).DateTime);
        var candidates = year is { } only ? db.Seasons.Where(s => s.Year == only) : db.Seasons.Where(s => s.Year != Season.TestYear);
        var years = await candidates
            .OrderBy(s => s.Year)
            .Select(s => new { s.Year, s.SlotsPerDay })
            .ToListAsync(cancellationToken);

        var platforms = await modes.GetAsync(cancellationToken);
        var seasons = new List<TodaySeason>();
        foreach (var season in years.Where(s => DaysOf(schedule, s.Year, today).Count > 0))
        {
            seasons.Add(await GetSeasonAsync(season.Year, season.SlotsPerDay, today, now, platforms, cancellationToken));
        }

        var queue = await outbox.GetStatusAsync(cancellationToken);
        var counts = await attention.GetAsync(cancellationToken);
        return new TodayReport(
            now,
            today,
            schedule.TimeZone,
            schedule.TimeZoneName,
            seasons,
            queue.Failed,
            new TodayQueues(counts.Requests, counts.PostClaims, counts.LinkChanges, queue.Waiting.Sum(w => w.Count), queue.Failed.Count));
    }

    /// <summary>
    /// The days of Advent a season has today and tomorrow, in its own calendar: December 1 to 25 for a real season (and
    /// November 30 has a tomorrow), and the rehearsal's days for the test season.
    /// </summary>
    public static IReadOnlyList<(int Day, bool IsToday)> DaysOf(AdventSchedule schedule, int year, DateOnly today) =>
        [.. new[] { (Date: today, IsToday: true), (Date: today.AddDays(1), IsToday: false) }
            .Select(d => (Day: schedule.GetDayOfAdvent(year, d.Date), d.IsToday))
            .Where(d => d.Day is not null)
            .Select(d => (d.Day!.Value, d.IsToday))];

    /// <summary>
    /// Where an announcement stands on a platform: what's been recorded, or else what happens next. Automatic platforms post
    /// at the post's time on its day, once its blurb check lets them (<see cref="SocialPostingService"/>); the rest are posted
    /// by hand after the reveal.
    /// </summary>
    public static TodayPostingStatus StatusOf(SocialPost? post, bool automatic, bool canAnnounce, DateTimeOffset now, DateTimeOffset revealsAt, DateTimeOffset announcedAt) =>
        post?.Status switch
        {
            SocialPostStatus.Posted => TodayPostingStatus.Posted,
            SocialPostStatus.Posting => TodayPostingStatus.Posting,
            SocialPostStatus.Failed => TodayPostingStatus.Failed,
            SocialPostStatus.Unconfirmed => TodayPostingStatus.Unconfirmed,
            _ when automatic && !canAnnounce => TodayPostingStatus.HeldForBlurb,
            _ when automatic => now >= announcedAt ? TodayPostingStatus.Due : TodayPostingStatus.Scheduled,
            _ => now >= revealsAt ? TodayPostingStatus.ToPost : TodayPostingStatus.ByHandLater,
        };

    private async Task<TodaySeason> GetSeasonAsync(
        int year,
        int slotsPerDay,
        DateOnly today,
        DateTimeOffset now,
        IReadOnlyList<PlatformPosting> platforms,
        CancellationToken cancellationToken)
    {
        var spots = await entries.ListAsync(year, cancellationToken);
        var checks = await blurbs.GetSeasonAsync(year, cancellationToken);

        // Off platforms are left off, as on the social page. The test season's entries never reach the real accounts.
        var shown = platforms.Where(p => p.Mode is not SocialPostingMode.Off).ToList();

        var days = new List<TodayDay>();
        var failed = new List<TodayFailedPost>();
        foreach (var (day, isToday) in DaysOf(schedule, year, today))
        {
            var announcements = await social.GetDayAsync(year, day, cancellationToken);
            var revealsAt = schedule.GetRevealTime(year, day);
            var date = schedule.GetDate(year, day);
            days.Add(new TodayDay(day, isToday, now >= revealsAt, [.. spots.Where(s => s.Day == day).Select(spot =>
            {
                var entry = announcements.Entries.FirstOrDefault(e => e.SlotId == spot.SlotId);
                var blurb = checks.GetValueOrDefault(spot.SlotId) ?? entry?.Blurb;
                var announcedAt = schedule.FromLocalTime(date.ToDateTime(posting.Value.TimeFor(spot.Part)));
                IReadOnlyList<TodayPosting> postings = entry is null
                    ? []
                    : [.. shown.Select(p =>
                    {
                        var post = entry.Posted.GetValueOrDefault(p.Platform);
                        var automatic = p.IsAutomatic && year != Season.TestYear;
                        return new TodayPosting(p.Platform, automatic, StatusOf(post, automatic, entry.CanAnnounce, now, revealsAt, announcedAt), post?.MarkedBy, post?.Error);
                    })];
                return new TodaySpot(spot, blurb, EntryAdminService.NeedsAttention(spot, blurb?.Status, isDayOver: false), announcedAt, postings);
            })]));

            if (isToday)
            {
                failed.AddRange(FailedPosts(announcements));
            }
        }

        // Yesterday's afternoon posts can fail after the evening's last look, so the morning digest names them too.
        if (schedule.GetDayOfAdvent(year, today.AddDays(-1)) is { } yesterday)
        {
            failed.InsertRange(0, FailedPosts(await social.GetDayAsync(year, yesterday, cancellationToken)));
        }

        var asked = (await backups.ListActiveOffersAsync(year, now, cancellationToken))
            .Where(o => now < schedule.GetDayEndTime(year, o.Day))
            .ToList();

        return new TodaySeason(year, slotsPerDay, days, asked, failed);
    }

    private static IEnumerable<TodayFailedPost> FailedPosts(AnnouncementDay day) =>
        day.Entries.SelectMany(entry => entry.Posted.Values
            .Where(p => p.Status is SocialPostStatus.Failed or SocialPostStatus.Unconfirmed)
            .OrderBy(p => p.Platform)
            .Select(p => new TodayFailedPost(entry.SlotId, day.Day, entry.Part, entry.Title, p.Platform, p.Status is SocialPostStatus.Unconfirmed, p.Error)));
}

/// <summary>What the Today panel and the morning digest call things, so the two always agree.</summary>
public static class TodayLabels
{
    /// <summary>"Posted", "Automatic at 10 AM", "To post by hand"...</summary>
    public static string Posting(TodayPosting posting, DateTimeOffset announcedAt, TimeZoneInfo timeZone) => posting.Status switch
    {
        TodayPostingStatus.Posted when posting.MarkedBy == SocialPostingService.Automation => "Posted automatically",
        TodayPostingStatus.Posted when posting.MarkedBy is { } who => $"Posted by {who}",
        TodayPostingStatus.Posted => "Posted",
        TodayPostingStatus.Posting => "Posting now",
        TodayPostingStatus.Failed => "Failed",
        TodayPostingStatus.Unconfirmed => "Did it go out?",
        TodayPostingStatus.Scheduled => $"Automatic at {Time(announcedAt, timeZone)}",
        TodayPostingStatus.Due => "Automatic, any minute",
        TodayPostingStatus.HeldForBlurb => "Waiting for its blurb check",
        TodayPostingStatus.ToPost => "To post by hand",
        _ => "By hand, after the reveal",
    };

    /// <summary>Full class names, so Tailwind generates them: green when done, red or amber when it needs someone, blue when it's on its way.</summary>
    public static string Badge(TodayPostingStatus status) => status switch
    {
        TodayPostingStatus.Posted => "bg-green-100 text-green-800",
        TodayPostingStatus.Failed => "bg-red-100 text-red-800",
        TodayPostingStatus.Unconfirmed or TodayPostingStatus.HeldForBlurb or TodayPostingStatus.ToPost => "bg-amber-100 text-amber-800",
        TodayPostingStatus.Scheduled or TodayPostingStatus.Due or TodayPostingStatus.Posting => "bg-advent-100 text-advent-800",
        _ => "bg-slate-100 text-slate-600",
    };

    /// <summary>Where a post's blurb check stands, in words for the digest.</summary>
    public static string Blurb(BlurbState blurb) => blurb.Status switch
    {
        BlurbStatus.Waiting when blurb.CheckedAt is null => "Blurb not checked yet",
        BlurbStatus.Waiting => "Blurb check waiting: the post may not be published yet",
        BlurbStatus.Passed => "Blurb passed",
        BlurbStatus.Missing => "Blurb missing",
        BlurbStatus.NeedsDecision => "Blurb needs your decision",
        BlurbStatus.CouldNotCheck => "Blurb couldn't be checked",
        _ => "Announced without the blurb",
    };

    /// <summary>Where a spot's post stands, as the entries list says it.</summary>
    public static string Entry(EntryListItem entry) => entry.Status switch
    {
        EntryStatus.Open when entry.DroppedOut => "Open (dropped out)",
        EntryStatus.Open => "Open",
        EntryStatus.ClaimedWithoutPost => "No post yet",
        EntryStatus.TitleOnly => "Title only, no link",
        EntryStatus.UrlOnly => "Link only, no title",
        _ => "Ready",
    };

    /// <summary>"10 AM", or "2:30 PM", in the reveal time zone.</summary>
    public static string Time(DateTimeOffset moment, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(moment, timeZone) is var local && local.Minute == 0
            ? local.ToString("h tt", CultureInfo.InvariantCulture)
            : local.ToString("h:mm tt", CultureInfo.InvariantCulture);

}
