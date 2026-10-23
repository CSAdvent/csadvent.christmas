using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Manage;

public sealed class SeasonAdminTests
{
    private static bool FinishedBefore2026(int year) => year < 2026;

    [Fact]
    public void OrganizersWorkOnTheEarliestSeasonThatIsNotFinished() =>
        Assert.Equal(2026, SeasonAdminService.CurrentYear([2024, 2025, 2026], FinishedBefore2026));

    [Fact]
    public void ASeasonCreatedEarlyForNextYearDoesNotReplaceTheCurrentOne() =>
        Assert.Equal(2026, SeasonAdminService.CurrentYear([2025, 2026, 2027], FinishedBefore2026));

    [Fact]
    public void OnceEverySeasonIsFinishedThereIsNoCurrentSeason() =>
        Assert.Null(SeasonAdminService.CurrentYear([2017, 2024, 2025], FinishedBefore2026));

    [Fact]
    public void WithoutSeasonsThereIsNoCurrentSeason() =>
        Assert.Null(SeasonAdminService.CurrentYear([], FinishedBefore2026));
}
