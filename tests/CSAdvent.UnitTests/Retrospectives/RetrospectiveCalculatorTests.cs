using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.Retrospectives;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Retrospectives;

public sealed class RetrospectiveCalculatorTests
{
    private static readonly AdventSchedule Schedule = new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System);

    private static int _slotId;

    private static PublishedPost Post(int year, int day, int part, string author, int? contributorId = null, string? title = null) =>
        new(++_slotId, year, day, part, title ?? $"Post {year}-{day}-{part}", author, contributorId);

    private static RetrospectiveInput Input(int year, IReadOnlyList<PublishedPost> posts, IReadOnlyList<RecordedHit>? hits = null) => new(
        year,
        SlotsPerDay: 2,
        Spots: 50,
        posts,
        hits ?? [],
        Schedule.GetDate(year, 1),
        moment => DateOnly.FromDateTime(Schedule.ToLocalTime(moment).DateTime),
        Requests: 0,
        RequestsFirstDay: 0,
        BackupsSteppedIn: 0,
        SocialPosts: [new PlatformPosts(SocialPlatform.Bluesky, 3), new PlatformPosts(SocialPlatform.Reddit, 0)]);

    [Fact]
    public void AuthorsAreFirstTimeOnceAndReturningAfterThatByNameOrGitHubAccount()
    {
        List<PublishedPost> posts =
        [
            Post(2024, 1, 1, "Ada Lovelace"),
            Post(2024, 1, 2, "Grace Hopper"),
            // Same person, different case and spacing.
            Post(2025, 1, 1, "ada  LOVELACE"),
            // A new name, but the same GitHub account as a 2025 post under another name.
            Post(2025, 1, 2, "Linus", contributorId: 7),
            Post(2026, 1, 1, "Linus Torvalds", contributorId: 7),
            // Two spots in one season are one author.
            Post(2026, 1, 2, "Grace Hopper"),
            Post(2026, 2, 1, "Grace Hopper"),
            Post(2026, 2, 2, "Barbara Liskov"),
        ];

        var stats = RetrospectiveCalculator.Calculate(Input(2026, posts));

        Assert.Equal(4, stats.Posts);
        Assert.Equal(3, stats.Authors);
        Assert.Equal(1, stats.FirstTimeAuthors);
        Assert.Equal(2, stats.ReturningAuthors);
        Assert.Equal(
            [new SeasonHistory(2024, 2, 2, 2), new SeasonHistory(2025, 2, 2, 1), new SeasonHistory(2026, 4, 3, 1)],
            stats.History);
        Assert.Equal(4, stats.AllTimeAuthors);
        Assert.Equal(8, stats.AllTimePosts);

        // Barbara is new, Linus wrote in 2025 too, and Grace in 2024.
        Assert.Equal([new SeasonsWritten(1, 1), new SeasonsWritten(2, 2)], stats.AuthorsBySeasonsWritten);
    }

    [Fact]
    public void LaterSeasonsAreLeftOut()
    {
        var stats = RetrospectiveCalculator.Calculate(Input(2025, [Post(2025, 1, 1, "Ada"), Post(2026, 1, 1, "Grace")]));

        Assert.Equal([new SeasonHistory(2025, 1, 1, 1)], stats.History);
        Assert.Equal(1, stats.Authors);
    }

    [Fact]
    public void ClicksAreCountedByDayInTheRevealTimeZoneAndBySource()
    {
        var first = Post(2026, 1, 1, "Ada", title: "First");
        var second = Post(2026, 3, 2, "Grace", title: "Second");
        List<RecordedHit> hits =
        [
            // 11 PM on December 2 in New York.
            new(first.SlotId, EntryHitKind.Click, "calendar", new DateTimeOffset(2026, 12, 3, 4, 0, 0, TimeSpan.Zero)),
            new(first.SlotId, EntryHitKind.Click, "bluesky", new DateTimeOffset(2026, 12, 3, 6, 0, 0, TimeSpan.Zero)),
            new(second.SlotId, EntryHitKind.Click, "site", new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero)),
            new(second.SlotId, EntryHitKind.Click, null, new DateTimeOffset(2026, 12, 31, 13, 0, 0, TimeSpan.Zero)),
            new(second.SlotId, EntryHitKind.Click, "calendar", new DateTimeOffset(2026, 12, 31, 14, 0, 0, TimeSpan.Zero)),
            new(first.SlotId, EntryHitKind.View, "bluesky", new DateTimeOffset(2026, 12, 3, 6, 0, 0, TimeSpan.Zero)),
        ];

        var stats = RetrospectiveCalculator.Calculate(Input(2026, [first, second], hits));

        Assert.True(stats.HasTraffic);
        Assert.Equal(5, stats.Clicks);
        Assert.Equal(1, stats.Views);
        Assert.Equal(31, stats.ClicksPerDay.Count);
        Assert.Equal(1, stats.ClicksPerDay[1]);
        Assert.Equal(1, stats.ClicksPerDay[2]);
        Assert.Equal(3, stats.ClicksPerDay[30]);
        Assert.Equal(5, stats.ClicksPerDay.Sum());

        // Untagged clicks and visits that arrived without a tag are the same thing: direct links.
        Assert.Equal(
            [new SourceClicks("calendar", 2), new SourceClicks(RetrospectiveCalculator.Direct, 2), new SourceClicks("bluesky", 1)],
            stats.ClicksBySource);
        Assert.Equal("Shared and direct links", stats.ClicksBySource[1].Label);

        Assert.Equal([new MostReadEntry(3, 2, "Second", "Grace", 3), new MostReadEntry(1, 1, "First", "Ada", 2)], stats.MostRead);
        Assert.Equal([new PlatformPosts(SocialPlatform.Bluesky, 3)], stats.SocialPosts);
    }

    [Fact]
    public void OnlyTheTenMostReadPostsAreListedWithTiesInCalendarOrder()
    {
        var posts = Enumerable.Range(1, 12).Select(day => Post(2026, day, 1, $"Author {day}")).ToList();
        var hits = posts
            .SelectMany(post => Enumerable.Repeat(new RecordedHit(post.SlotId, EntryHitKind.Click, "calendar", new DateTimeOffset(2026, 12, 20, 12, 0, 0, TimeSpan.Zero)), post.Day == 12 ? 5 : 1))
            .ToList();

        var stats = RetrospectiveCalculator.Calculate(Input(2026, posts, hits));

        Assert.Equal(RetrospectiveCalculator.MostReadCount, stats.MostRead.Count);
        Assert.Equal([12, 1, 2, 3, 4, 5, 6, 7, 8, 9], stats.MostRead.Select(e => e.Day));
    }

    [Fact]
    public void SeasonsWithoutTrafficHaveOnlyTheirPostsAndAuthors()
    {
        var stats = RetrospectiveCalculator.Calculate(Input(2019, [Post(2019, 1, 1, "Ada")]));

        Assert.False(stats.HasTraffic);
        Assert.Empty(stats.ClicksPerDay);
        Assert.Empty(stats.ClicksBySource);
        Assert.Empty(stats.MostRead);
    }

    [Fact]
    public void TheNumbersSurviveBeingStored()
    {
        var first = Post(2026, 1, 1, "Ada", title: "Records & `Span<T>`");
        var stats = RetrospectiveCalculator.Calculate(Input(2026, [Post(2025, 1, 1, "Ada"), first],
            [new(first.SlotId, EntryHitKind.Click, "mastodon", new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero))]));

        var stored = RetrospectiveStats.FromJson(stats.ToJson());

        Assert.Equal(stats.ToJson(), stored.ToJson());
        Assert.Equal(stats.MostRead, stored.MostRead);
        Assert.Equal(stats.History, stored.History);
        Assert.Equal(SocialPlatform.Bluesky, Assert.Single(stored.SocialPosts).Platform);
        Assert.Contains("\"Bluesky\"", stats.ToJson());
    }
}
