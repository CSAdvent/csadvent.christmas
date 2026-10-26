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
    public void ASeasonIsInTheArchiveOnceItsLastDayIsRevealed()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 24, 23, 59, 59, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);

        Assert.False(schedule.IsLastDayRevealed(2026));
        Assert.True(schedule.IsLastDayRevealed(2025));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(schedule.IsLastDayRevealed(2026));
    }

    [Fact]
    public void ASeasonIsFinishedOnceItsLastDayIsOver()
    {
        // Noon on Christmas Day: the last day is revealed, and its authors can still add their posts on My spots.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);

        Assert.True(schedule.IsLastDayRevealed(2026));
        Assert.False(schedule.IsFinished(2026));
        Assert.True(schedule.IsFinished(2025));

        clock.SetUtcNow(new DateTimeOffset(2026, 12, 25, 23, 59, 59, TimeSpan.FromHours(-5)));
        Assert.False(schedule.IsFinished(2026));

        // Midnight on December 26.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(schedule.IsFinished(2026));
    }

    [Fact]
    public void ClaimsOpenAtTheScheduledTimeAndNeverIfUnscheduled()
    {
        var opensAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var clock = new FakeTimeProvider(opensAt.AddSeconds(-1));
        var schedule = CreateSchedule(clock);

        Assert.False(schedule.AreClaimsOpen(opensAt));
        Assert.False(schedule.AreClaimsOpen(null));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(schedule.AreClaimsOpen(opensAt));
        Assert.False(schedule.AreClaimsOpen(null));
    }

    [Theory]
    // Sign-ups open at midnight Eastern on November 1, 2026 (still daylight time); the announcements go from 9 AM (standard time).
    [InlineData("2026-10-31T10:00:00-04:00", false)]
    [InlineData("2026-11-01T08:59:00-05:00", false)]
    [InlineData("2026-11-01T09:00:00-05:00", true)]
    [InlineData("2026-11-02T08:00:00-05:00", false)]
    [InlineData("2026-11-02T23:00:00-05:00", true)]
    [InlineData("2026-11-03T09:30:00-05:00", false)]
    public void SignUpsAreAnnouncedFromTheMorningSendTimeForTwoDays(string now, bool due)
    {
        var opensAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.Equal(due, CreateSchedule().IsTimeToAnnounceSignUps(opensAt, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(9, 0)));
    }

    [Fact]
    public void UnscheduledSignUpsAreNeverAnnounced() =>
        Assert.False(CreateSchedule().IsTimeToAnnounceSignUps(null, new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), new TimeOnly(9, 0)));

    [Fact]
    public void TodayIsTheDateInTheRevealTimeZone()
    {
        // 03:00 UTC on December 2 is still 10 PM on December 1 in New York.
        var schedule = CreateSchedule(new FakeTimeProvider(new DateTimeOffset(2026, 12, 2, 3, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 12, 1), schedule.Today);
    }

    [Theory]
    [InlineData(2026, 12, 15, 9, 30, 14)] // Eastern Standard Time, UTC-5
    [InlineData(2026, 7, 4, 12, 0, 16)] // Eastern Daylight Time, UTC-4
    public void ConvertsLocalTimesInTheRevealTimeZoneToUtc(int year, int month, int day, int hour, int minute, int expectedUtcHour)
    {
        var utc = CreateSchedule().FromLocalTime(new DateTime(year, month, day, hour, minute, 0));

        Assert.Equal(new DateTimeOffset(year, month, day, expectedUtcHour, minute, 0, TimeSpan.Zero), utc);
        Assert.Equal(TimeSpan.Zero, utc.Offset);
    }

    [Fact]
    public void ADayEndsAtTheNextMidnightInTheRevealTimeZone()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 25, 23, 59, 59, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);

        Assert.Equal(new DateTimeOffset(2026, 12, 26, 5, 0, 0, TimeSpan.Zero), schedule.GetDayEndTime(2026, 25));
        Assert.False(schedule.IsDayOver(2026, 25));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(schedule.IsDayOver(2026, 25));
    }

    [Fact]
    public void DeletesContactDetailsAtTheStartOfFebruaryAfterTheSeason() =>
        // Midnight Eastern Standard Time on February 1.
        Assert.Equal(new DateTimeOffset(2027, 2, 1, 5, 0, 0, TimeSpan.Zero), CreateSchedule().GetContactDeletionTime(2026));

    [Fact]
    public void TheHomePageShowsTheCalendarFromWhenSignUpsOpen()
    {
        var opensAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));
        var clock = new FakeTimeProvider(opensAt.AddSeconds(-1));
        var schedule = CreateSchedule(clock);

        Assert.False(schedule.IsCalendarShown(2026, opensAt));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(schedule.IsCalendarShown(2026, opensAt));
    }

    [Fact]
    public void WithoutSignUpsTheCalendarStartsAtTheFirstReveal()
    {
        var schedule = CreateSchedule();

        Assert.Equal(schedule.GetRevealTime(2026, 1), schedule.GetCalendarStart(2026, claimsOpenAt: null));
        Assert.Equal(schedule.GetRevealTime(2026, 1), schedule.GetCalendarStart(2026, new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheCalendarStaysUpUntilTheEndOfDecember()
    {
        // Christmas Day, after the last reveal: the calendar is complete, and people are still reading.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 25, 9, 0, 0, TimeSpan.FromHours(-5)));
        var schedule = CreateSchedule(clock);
        var opensAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.True(schedule.IsLastDayRevealed(2026));
        Assert.False(schedule.IsFinished(2026));
        Assert.True(schedule.IsCalendarShown(2026, opensAt));

        clock.SetUtcNow(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.FromHours(-5)));
        Assert.True(schedule.IsCalendarShown(2026, opensAt));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(schedule.IsCalendarShown(2026, opensAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void RejectsDaysOutsideAdvent(int day) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSchedule().GetRevealTime(2026, day));
}
