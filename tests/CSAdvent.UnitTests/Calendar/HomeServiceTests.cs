using CSAdvent.Web.Calendar;

namespace CSAdvent.UnitTests.Calendar;

public sealed class HomeServiceTests
{
    private static bool OverBefore2026(int year) => year < 2026;

    private static readonly DateTimeOffset November1 = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

    private static readonly SeasonDates[] Seasons = [new(2024, null), new(2025, null), new(2026, November1)];

    private static HomePlan PlanAt(DateTimeOffset now, params SeasonDates[] seasons) =>
        HomeService.Plan(seasons.Length > 0 ? seasons : Seasons, CalendarFixtures.Schedule(now));

    [Fact]
    public void ShowsTheCalendarOnceSignUpsOpen()
    {
        var plan = PlanAt(November1);

        Assert.Equal(new HomePlan(2026, LastYear: null, NextSeason: null), plan);
    }

    [Fact]
    public void BeforeSignUpsOpenItIsBetweenSeasons()
    {
        var plan = PlanAt(November1.AddDays(-30));

        Assert.Null(plan.CalendarYear);
        Assert.Equal(2025, plan.LastYear);
        Assert.Equal(new UpcomingSeason(2026, new DateOnly(2026, 11, 1), DaysUntilSignUps: 30), plan.NextSeason);
    }

    [Fact]
    public void TheCountdownStartsOnSeptember1()
    {
        var august31 = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.FromHours(-4));

        Assert.Null(PlanAt(august31).NextSeason!.DaysUntilSignUps);
        Assert.Equal(61, PlanAt(august31.AddDays(1)).NextSeason!.DaysUntilSignUps);
    }

    [Fact]
    public void TheCountdownReachesZeroOnTheDaySignUpsOpen()
    {
        var opensAtNoon = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(-5));
        var morning = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(-5));

        var plan = PlanAt(morning, new(2025, null), new(2026, opensAtNoon));

        Assert.Equal(0, plan.NextSeason!.DaysUntilSignUps);
    }

    [Fact]
    public void WithoutASignUpDateThereIsNoCountdown()
    {
        var plan = PlanAt(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), new(2025, null), new(2026, null));

        Assert.Equal(new UpcomingSeason(2026, SignUpsOpenOn: null, DaysUntilSignUps: null), plan.NextSeason);
    }

    [Fact]
    public void TheCalendarStaysUpUntilTheEndOfDecember()
    {
        // A 2027 season created early doesn't replace this year's calendar.
        var plan = PlanAt(new DateTimeOffset(2026, 12, 28, 12, 0, 0, TimeSpan.FromHours(-5)), new(2026, November1), new(2027, null));

        Assert.Equal(2026, plan.CalendarYear);
    }

    [Fact]
    public void InJanuaryItThanksEveryoneForTheSeasonThatJustEnded()
    {
        var plan = PlanAt(new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.FromHours(-5)), new(2025, null), new(2026, November1));

        Assert.Equal(new HomePlan(CalendarYear: null, LastYear: 2026, NextSeason: null), plan);
    }

    [Fact]
    public void HasNothingToShowWithoutSeasons()
    {
        Assert.Equal(new HomePlan(null, null, null), HomeService.Plan([], CalendarFixtures.Schedule(November1)));
    }

    [Fact]
    public void OrganizersWorkOnTheCurrentSeason()
    {
        Assert.Equal((2026, false), HomeService.SelectSeason([2024, 2025, 2026], OverBefore2026));
    }

    [Fact]
    public void ASeasonCreatedEarlyForNextYearDoesNotReplaceTheCurrentOne()
    {
        Assert.Equal((2026, false), HomeService.SelectSeason([2025, 2026, 2027], OverBefore2026));
    }

    [Fact]
    public void OffSeasonOrganizersWorkOnTheLatestSeason()
    {
        Assert.Equal((2025, true), HomeService.SelectSeason([2017, 2024, 2025], OverBefore2026));
    }
}
