using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Calendar;

public sealed class SeasonCalendarTests
{
    // Midday Eastern on December 10, 2026: days 1-10 are revealed, 11-25 aren't.
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.FromHours(-5)));

    private static readonly AdventSchedule Schedule = new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), Clock);

    [Fact]
    public void GroupsEntriesByDayInPartOrder()
    {
        var calendar = SeasonCalendar.From(Season.Create(2026, slotsPerDay: 2, Clock.GetUtcNow()), Schedule);

        Assert.Equal(Enumerable.Range(1, 25), calendar.Days.Select(d => d.Day));
        Assert.All(calendar.Days, day => Assert.Equal([1, 2], day.Entries.Select(e => e.Part)));
        Assert.Equal(Schedule.GetRevealTime(2026, 6), calendar.Days[5].RevealsAt);
    }

    [Fact]
    public void ShowsPostsOnlyOnceTheyAreRevealed()
    {
        var season = Season.Create(2026, slotsPerDay: 1, Clock.GetUtcNow());
        foreach (var slot in season.Slots.Where(s => s.Day is 10 or 11))
        {
            slot.AuthorName = "Ada";
            slot.PostTitle = $"Day {slot.Day}";
            slot.PostUrl = $"https://example.com/{slot.Day}";
        }

        var calendar = SeasonCalendar.From(season, Schedule);

        var revealed = calendar.Days[9].Entries.Single();
        Assert.True(revealed.IsRevealed);
        Assert.Equal(("Day 10", "https://example.com/10"), (revealed.PostTitle, revealed.PostUrl));

        var tomorrow = calendar.Days[10].Entries.Single();
        Assert.False(tomorrow.IsRevealed);
        Assert.Null(tomorrow.PostTitle);
        Assert.Null(tomorrow.PostUrl);
        Assert.Equal("Ada", tomorrow.AuthorName);

        Assert.Equal(1, calendar.PostCount);
    }

    [Fact]
    public void TheNextRevealCountsDownOnceItsWithinADay()
    {
        var season = Season.Create(2026, slotsPerDay: 2, Clock.GetUtcNow());

        // Midday on December 10: December 11 is revealed in 12 hours.
        var calendar = SeasonCalendar.From(season, Schedule);
        Assert.Equal(11, calendar.CountingDownTo?.Day);
        Assert.Equal(Clock.GetUtcNow(), calendar.GeneratedAt);

        // Two weeks before Advent, December 1 is too far off.
        var early = SeasonCalendar.From(season, new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(-5)))));
        Assert.Null(early.CountingDownTo);

        // After December 25 there's nothing left to reveal.
        var after = SeasonCalendar.From(season, new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.FromHours(-5)))));
        Assert.Null(after.CountingDownTo);
    }

    [Fact]
    public void AuthorsWithoutAWebsiteLinkToTheirGitHubProfile()
    {
        var season = Season.Create(2026, slotsPerDay: 2, Clock.GetUtcNow());
        var github = new Contributor { GithubLogin = "ada-dev" };
        Claim(season, part: 1, "Ada", website: null, github);
        Claim(season, part: 2, "Grace", website: "https://grace.dev", github);

        var day = SeasonCalendar.From(season, Schedule).Days[0];

        Assert.Equal(["https://github.com/ada-dev", "https://grace.dev"], day.Entries.Select(e => e.AuthorUrl));
    }

    private static void Claim(Season season, int part, string name, string? website, Contributor contributor)
    {
        var slot = season.Slots.Single(s => s.Day == 1 && s.Part == part);
        slot.AuthorName = name;
        slot.AuthorUrl = website;
        slot.Contributor = contributor;
    }
}
