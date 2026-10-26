using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Manage;

/// <summary>
/// A day for the Today panel and the morning digest (#171): December 6 at 8 AM Eastern, with one post ready and one without a
/// link today, and tomorrow's post in but not revealed.
/// </summary>
internal static class TodayFixtures
{
    public static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static readonly DateTimeOffset Now = new(2026, 12, 6, 13, 0, 0, TimeSpan.Zero);

    public const string HiddenTitle = "Tomorrow's secret title";

    public const string HiddenUrl = "https://example.com/tomorrows-secret";

    public static TodaySeason SampleSeason(int year = 2026, IReadOnlyList<ActiveBackupOffer>? backups = null, IReadOnlyList<TodayFailedPost>? failed = null) => new(
        year,
        2,
        [
            new TodayDay(6, IsToday: true, IsRevealed: true,
            [
                new TodaySpot(
                    new EntryListItem(41, 6, 1, "Ada Lovelace", "ada@example.com", false, "Span<T> all the way down", "https://example.com/span", EntryStatus.Ready),
                    new BlurbState(BlurbStatus.Passed, null, Now, Now, null, false),
                    NeedsAttention: false,
                    At(6, 10),
                    [
                        new TodayPosting(SocialPlatform.Bluesky, true, TodayPostingStatus.Posted, "automation"),
                        new TodayPosting(SocialPlatform.Mastodon, true, TodayPostingStatus.Scheduled),
                        new TodayPosting(SocialPlatform.Facebook, true, TodayPostingStatus.Failed, "automation", "401 Unauthorized"),
                        new TodayPosting(SocialPlatform.LinkedIn, false, TodayPostingStatus.ToPost),
                    ]),
                new TodaySpot(
                    new EntryListItem(42, 6, 2, "Grace Hopper", "grace@example.com", false, null, null, EntryStatus.ClaimedWithoutPost),
                    null,
                    NeedsAttention: true,
                    At(6, 14),
                    []),
            ]),
            new TodayDay(7, IsToday: false, IsRevealed: false,
            [
                new TodaySpot(
                    new EntryListItem(43, 7, 1, "Linus Torvalds", "linus@example.com", false, HiddenTitle, HiddenUrl, EntryStatus.Ready),
                    new BlurbState(BlurbStatus.Waiting, null, Now, null, null, false),
                    NeedsAttention: false,
                    At(7, 10),
                    [new TodayPosting(SocialPlatform.LinkedIn, false, TodayPostingStatus.ByHandLater)]),
                new TodaySpot(new EntryListItem(44, 7, 2, null, null, true, null, null, EntryStatus.Open), null, NeedsAttention: true, At(7, 14), []),
            ]),
        ],
        backups ?? [new ActiveBackupOffer(44, 3, 7, 2, "Barbara Liskov", false, Now.AddHours(-1), Now.AddHours(3))],
        failed ?? [new TodayFailedPost(39, 5, 2, "Records, revisited", SocialPlatform.X, true, "X didn't answer in time.")]);

    public static TodayReport SampleReport(TodaySeason? season = null, IReadOnlyList<FailedEmail>? failedEmails = null) => new(
        Now,
        new DateOnly(2026, 12, 6),
        Eastern,
        season is null ? [] : [season],
        failedEmails ?? [Failed(1, "daily-share")],
        new TodayQueues(Requests: 2, PostClaims: 1, LinkChanges: 0, EmailsWaiting: 12, FailedEmails: failedEmails?.Count ?? 1));

    public static FailedEmail Failed(int id, string kind) =>
        new(id, kind, $"reader-{id}@example.com", "Day 5 of C# Advent", 6, "The email service answered 503", Now.AddHours(-1));

    /// <summary>A time on a December 2026 day, in Eastern (standard) time.</summary>
    private static DateTimeOffset At(int day, int hour) => new(2026, 12, day, hour, 0, 0, TimeSpan.FromHours(-5));
}
