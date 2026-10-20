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
}
