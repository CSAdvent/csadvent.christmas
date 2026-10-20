using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Data;

public sealed class SeasonTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAddsAnOpenSlotForEveryDayAndPart()
    {
        var season = Season.Create(2026, slotsPerDay: 2, Now);

        Assert.Equal(
            Enumerable.Range(1, 25).SelectMany(day => new[] { (day, 1), (day, 2) }),
            season.Slots.Select(s => (s.Day, s.Part)));
        Assert.All(season.Slots, s => Assert.Equal(2026, s.SeasonYear));
        Assert.All(season.Slots, s => Assert.False(s.IsClaimed));
    }

    [Fact]
    public void CreateSupportsOneSlotPerDayLikeTheFirstSeason()
    {
        var season = Season.Create(2017, slotsPerDay: 1, Now);

        Assert.Equal(Enumerable.Range(1, 25), season.Slots.Select(s => s.Day));
        Assert.All(season.Slots, s => Assert.Equal(1, s.Part));
    }

    [Theory]
    [InlineData(2016, 2)]
    [InlineData(2026, 0)]
    [InlineData(2026, 3)]
    public void CreateRejectsInvalidSeasons(int year, int slotsPerDay) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Season.Create(year, slotsPerDay, Now));
}
