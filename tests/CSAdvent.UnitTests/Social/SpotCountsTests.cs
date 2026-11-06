using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Social;

public sealed class SpotCountsTests
{
    [Theory]
    [InlineData(new[] { 24 }, "December 24")]
    [InlineData(new[] { 3, 24 }, "December 3 and 24")]
    [InlineData(new[] { 3, 9, 14, 20, 24 }, "December 3, 9, 14, 20 and 24")]
    public void TheDaysLeftAreListedInOneLine(int[] days, string expected) =>
        Assert.Equal(expected, new SpotCounts(50, 50 - days.Length, days.Length, days).Days);

    [Theory]
    [InlineData(0, 50, "Sign-ups are open")]
    [InlineData(12, 38, "38 of 50 spots left")]
    [InlineData(30, 20, "20 of 50 spots left")]
    [InlineData(45, 5, "Only 5 spots left")]
    [InlineData(49, 1, "Just one spot left")]
    public void TheHeadlineSaysHowManyAreLeft(int taken, int left, string expected) =>
        Assert.Equal(expected, new SpotCounts(50, taken, left, [.. Enumerable.Range(1, Math.Min(left, 25))]).Headline);

    [Fact]
    public void HalfTakenIsFillingUp()
    {
        Assert.False(new SpotCounts(50, 24, 26, [.. Enumerable.Range(1, 13)]).AreFillingUp);
        Assert.True(new SpotCounts(50, 25, 25, [.. Enumerable.Range(1, 13)]).AreFillingUp);
    }
}
