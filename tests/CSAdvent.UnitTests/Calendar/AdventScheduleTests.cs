using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Calendar;

public sealed class AdventScheduleTests
{
    private static AdventSchedule CreateSchedule(TimeProvider? timeProvider = null, string timeZone = "America/New_York") =>
        new(Options.Create(new AdventOptions { TimeZone = timeZone }), timeProvider ?? TimeProvider.System);

    [Fact]
    public void RevealsAtMidnightInTheConfiguredTimeZone()
    {
        var schedule = CreateSchedule();

        var reveal = schedule.GetRevealTime(2026, 1);

        // Midnight Eastern Standard Time, not midnight UTC (7 PM Eastern the evening before).
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 5, 0, 0, TimeSpan.Zero), reveal);
        Assert.Equal(TimeSpan.FromHours(-5), reveal.Offset);
    }

    [Fact]
    public void FollowsTheConfiguredTimeZone()
    {
        var schedule = CreateSchedule(timeZone: "Europe/London");

        Assert.Equal(new DateTimeOffset(2026, 12, 25, 0, 0, 0, TimeSpan.Zero), schedule.GetRevealTime(2026, 25));
    }

    [Fact]
    public void RevealsEachDayAtExactlyMidnight()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);

        Assert.False(schedule.IsRevealed(2026, 1));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(schedule.IsRevealed(2026, 1));
        Assert.False(schedule.IsRevealed(2026, 2));
    }

    [Fact]
    public void RevealsASlotByItsSeasonAndDay()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var schedule = CreateSchedule(clock);
        var season = Season.Create(2026, slotsPerDay: 2, clock.GetUtcNow());

        Assert.True(schedule.IsRevealed(season.Slots.First(s => s.Day == 10)));
        Assert.False(schedule.IsRevealed(season.Slots.First(s => s.Day == 11)));
    }

    [Fact]
    public void ASeasonIsOverOnceItsLastDayIsRevealed()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 24, 23, 59, 59, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);

        Assert.False(schedule.IsSeasonOver(2026));
        Assert.True(schedule.IsSeasonOver(2025));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(schedule.IsSeasonOver(2026));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void RejectsDaysOutsideAdvent(int day) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSchedule().GetRevealTime(2026, day));
}
