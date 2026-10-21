using CSAdvent.Web.Calendar;

namespace CSAdvent.UnitTests.Calendar;

public sealed class HomeServiceTests
{
    private static bool OverBefore2026(int year) => year < 2026;

    [Fact]
    public void ShowsTheCurrentSeason()
    {
        Assert.Equal((2026, false), HomeService.SelectSeason([2024, 2025, 2026], OverBefore2026));
    }

    [Fact]
    public void ASeasonCreatedEarlyForNextYearDoesNotReplaceTheCurrentOne()
    {
        Assert.Equal((2026, false), HomeService.SelectSeason([2025, 2026, 2027], OverBefore2026));
    }

    [Fact]
    public void ShowsTheLatestSeasonOffSeason()
    {
        Assert.Equal((2025, true), HomeService.SelectSeason([2017, 2024, 2025], OverBefore2026));
    }

    [Fact]
    public void HasNothingToShowWithoutSeasons()
    {
        Assert.Equal((null, true), HomeService.SelectSeason([], OverBefore2026));
    }
}
