using Bunit;
using CSAdvent.Web.Components.Retrospectives;
using CSAdvent.Web.Retrospectives;

namespace CSAdvent.UnitTests.Retrospectives;

public sealed class RetrospectiveChartTests : BunitContext
{
    [Theory]
    [InlineData(0, new[] { 0, 1 })]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(7, new[] { 0, 2, 4, 6, 8 })]
    [InlineData(50, new[] { 0, 20, 40, 60 })]
    [InlineData(1500, new[] { 0, 500, 1000, 1500 })]
    [InlineData(1234, new[] { 0, 500, 1000, 1500 })]
    public void AxesUseRoundTicks(int max, int[] ticks) => Assert.Equal(ticks, ChartScale.Ticks(max));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(750, "750")]
    [InlineData(1000, "1K")]
    [InlineData(1500, "1.5K")]
    [InlineData(12345, "12.3K")]
    public void TicksAreCompact(int value, string text) => Assert.Equal(text, ChartScale.Compact(value));

    [Fact]
    public void ByTheNumbersChartsTrafficAuthorsAndPosts()
    {
        var numbers = Render<RetrospectiveNumbers>(p => p
            .Add(c => c.Stats, RetrospectiveDraftTests.Stats())
            .Add(c => c.StatsAt, new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.FromHours(-5))));

        Assert.Equal(
            ["Posts", "Authors", "First-time authors", "Clicks through to posts"],
            numbers.FindAll("dt").Select(dt => dt.TextContent));
        Assert.Equal(
            ["Clicks each day in December", "Where the clicks came from", "First-time and returning authors each season", "How many seasons 2026's authors have written for", "Posts each season"],
            numbers.FindAll("figcaption > span:first-child").Select(span => span.TextContent));
        Assert.Contains("as of January 2, 2027", numbers.Markup);

        // Clicks per day: one column a day, the busiest labeled and highlighted, every value in the table.
        var days = numbers.FindAll("figure")[0];
        Assert.Equal(31, days.QuerySelectorAll("tbody tr").Length);
        Assert.Equal("1,500", Assert.Single(days.QuerySelectorAll("span.bottom-full")).TextContent);
        Assert.Single(days.QuerySelectorAll("div.bg-advent-600"));
        Assert.Equal(30, days.QuerySelectorAll("div.bg-advent-300").Length);
        Assert.Contains("title=\"December 3: 1,500 clicks\"", days.OuterHtml);

        // Sources: a bar and its value per row.
        var sources = numbers.FindAll("figure")[1];
        Assert.Equal(["The calendar", "Bluesky", "The daily email", "RSS"], sources.QuerySelectorAll("li > span:first-child").Select(s => s.TextContent));
        Assert.Contains("width: calc((100% - 4rem) * 0.5)", sources.OuterHtml);

        // Authors: two stacked series with a legend, and each season's split in the table.
        var authors = numbers.FindAll("figure")[2];
        Assert.Equal(["Returning authors", "First-time authors"], authors.QuerySelectorAll("ul li").Select(li => li.TextContent));
        Assert.Contains("title=\"2025: 26 returning authors, 20 first-time authors\"", authors.OuterHtml);
        Assert.Equal(["’17", "’25", "’26"], authors.QuerySelectorAll("div.mt-1 > div").Select(d => d.TextContent));
    }

    [Fact]
    public void SeasonsWithoutTrafficChartOnlyTheirAuthorsAndPosts()
    {
        var numbers = Render<RetrospectiveNumbers>(p => p
            .Add(c => c.Stats, RetrospectiveDraftTests.Stats(2019, traffic: false))
            .Add(c => c.StatsAt, DateTimeOffset.UtcNow));

        Assert.Equal(["Posts", "Authors", "First-time authors", "Authors since 2017"], numbers.FindAll("dt").Select(dt => dt.TextContent));
        Assert.Equal(3, numbers.FindAll("figure").Count);
        Assert.DoesNotContain("Clicks", numbers.Markup);
        Assert.Contains("Visits and clicks were first counted in 2026", numbers.Markup);
    }
}
