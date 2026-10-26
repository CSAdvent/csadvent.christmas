using Bunit;
using CSAdvent.Web.Components.Calendar;
using Microsoft.AspNetCore.Http;

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

        // Through the tracked redirect, so the click is counted, never straight to the post.
        Assert.Equal("go/2026/10/1?via=calendar", day10.QuerySelector("a[href^='go/']")!.GetAttribute("href"));
        Assert.DoesNotContain("https://example.com/ada\"", day10.InnerHtml);
    }

    [Fact]
    public void LabelsLinkRevealedPostsToTheirPages()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        // A link search engines can follow to the entry's page (#167), while the title goes through the tracked redirect.
        var label = grid.Find("#december-10 li > p:first-child a");
        Assert.Equal("Post 1", label.TextContent);
        Assert.Equal("2026/day/10/1", label.GetAttribute("href"));

        // Not a spot without a post, and nothing before the reveal (#5).
        Assert.Single(grid.FindAll("#december-10 a[href^='2026/day/']"));
        Assert.Empty(grid.FindAll("#december-11 a[href^='2026/day/']"));
    }

    [Fact]
    public void WithOnePostADayTheDayLinksThePostsPage()
    {
        var season = Web.Data.Season.Create(2017, slotsPerDay: 1, CalendarFixtures.December10);
        var slot = season.Slots.Single(s => s.Day == 3);
        slot.AuthorName = "Matt";
        slot.PostTitle = "The first one";
        slot.PostUrl = "https://example.com/first";
        var calendar = Web.Calendar.SeasonCalendar.From(season, CalendarFixtures.Schedule(CalendarFixtures.December10));

        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, calendar));

        Assert.Equal("2017/day/3/1", grid.Find("#december-3 h2 a").GetAttribute("href"));
        Assert.Empty(grid.FindAll("#december-4 h2 a"));
    }

    [Fact]
    public void TomorrowCountsDownToItsReveal()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        // Midday on December 10: December 11 is revealed at midnight Eastern, 12 hours away, and nothing else counts down.
        var countdown = Assert.Single(grid.FindAll("time[data-countdown]"));
        Assert.Equal("12:00:00", countdown.TextContent);
        Assert.Equal("2026-12-11T00:00:00.0000000-05:00", countdown.GetAttribute("data-countdown"));
        Assert.Equal("2026-12-10T12:00:00.0000000-05:00", countdown.GetAttribute("data-now"));
        Assert.NotNull(grid.Find("#december-11").QuerySelector("time[data-countdown]"));
    }

    [Fact]
    public void APageWithACountdownIsNeverKept()
    {
        // The countdown carries the server's time, which a copy the browser kept would get wrong.
        HttpContext counting = new DefaultHttpContext();
        Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)).AddCascadingValue(counting));
        Assert.Equal("no-cache, no-store", counting.Response.Headers.CacheControl.ToString());

        // Once every day is revealed there's nothing to count down to, and the page can be kept as usual.
        HttpContext finished = new DefaultHttpContext();
        var afterwards = new DateTimeOffset(2027, 1, 5, 12, 0, 0, TimeSpan.FromHours(-5));
        Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened, afterwards)).AddCascadingValue(finished));
        Assert.Equal(0, finished.Response.Headers.CacheControl.Count);
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
    public void LabelsEachSpotSoYouKnowWhichOneYouAreClaiming()
    {
        var day11 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened))).Find("#december-11");

        Assert.Equal(["Post 1", "Post 2"], day11.QuerySelectorAll("li li > p:first-child").Select(p => p.TextContent));
        Assert.Equal("Claim December 11, post 2", day11.QuerySelector("a[href^='claim/']")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void DoesNotLabelDaysWithASinglePost()
    {
        var season = Web.Data.Season.Create(2017, slotsPerDay: 1, CalendarFixtures.December10);
        var calendar = Web.Calendar.SeasonCalendar.From(season, CalendarFixtures.Schedule(CalendarFixtures.December10));

        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, calendar));

        Assert.DoesNotContain("Post 1", grid.Markup);
    }

    [Fact]
    public void HighlightsToday()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        Assert.Contains("Today", grid.Find("#december-10").TextContent);
        Assert.DoesNotContain("Today", grid.Find("#december-11").TextContent);
    }
}
