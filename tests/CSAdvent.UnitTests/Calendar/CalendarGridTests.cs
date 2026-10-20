using Bunit;
using CSAdvent.Web.Components.Calendar;

namespace CSAdvent.UnitTests.Calendar;

public sealed class CalendarGridTests : BunitContext
{
    private static readonly DateTimeOffset ClaimsOpened = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void ShowsRevealedPostsWithTheirAuthors()
    {
        var day10 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened))).Find("#december-10");

        Assert.Contains("Pattern matching all the things", day10.TextContent);
        Assert.Contains("by Ada", day10.TextContent);
    }

    [Fact]
    public void DoesNotNameAuthorsWhoNeverPublished()
    {
        var day10 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened))).Find("#december-10");

        Assert.Contains("No post", day10.TextContent);
        Assert.DoesNotContain("Bob", day10.TextContent);
    }

    [Fact]
    public void ShowsWhoClaimedASpotButNotTheirPostBeforeTheReveal()
    {
        var day11 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened))).Find("#december-11");

        Assert.Contains("Claimed by Carol", day11.TextContent);
        Assert.Equal("https://example.com/carol", day11.QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("Not revealed yet", day11.TextContent);
        Assert.DoesNotContain("https://example.com/carol/post", day11.InnerHtml);
    }

    [Fact]
    public void OffersOpenSpotsWhileClaimsAreOpen()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        Assert.Equal("claim/2026/11/2", grid.Find("#december-11 a[href^='claim/']").GetAttribute("href"));
        // Revealed days can't be claimed any more.
        Assert.Empty(grid.FindAll("#december-9 a[href^='claim/']"));
    }

    [Fact]
    public void ShowsOpenSpotsWithoutClaimLinksBeforeClaimsOpen()
    {
        var notYet = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(-4));
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened, now: notYet)));

        Assert.Empty(grid.FindAll("a[href^='claim/']"));
        Assert.Contains("Open", grid.Find("#december-11").TextContent);
    }

    [Fact]
    public void HighlightsToday()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        Assert.Contains("Today", grid.Find("#december-10").TextContent);
        Assert.DoesNotContain("Today", grid.Find("#december-11").TextContent);
    }
}
