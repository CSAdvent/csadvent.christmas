using Bunit;
using CSAdvent.Web.Calendar;
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
    public void LabelsArePlainTextBecauseThePostsPageGoesToThePostToo()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        // The entry page's link goes straight to the post now (decided 2026-10-26), like the title, so the label isn't a link.
        Assert.Equal("Post 1", grid.Find("#december-10 li > p:first-child").TextContent);
        Assert.Empty(grid.FindAll("a[href*='/day/']"));
        Assert.Equal("go/2026/10/1?via=calendar", Assert.Single(grid.FindAll("#december-10 a")).GetAttribute("href"));
    }

    [Fact]
    public void WithOnePostADayTheDayIsPlainTextToo()
    {
        var season = Web.Data.Season.Create(2017, slotsPerDay: 1, CalendarFixtures.December10);
        var slot = season.Slots.Single(s => s.Day == 3);
        slot.AuthorName = "Matt";
        slot.PostTitle = "The first one";
        slot.PostUrl = "https://example.com/first";
        var calendar = Web.Calendar.SeasonCalendar.From(season, CalendarFixtures.Schedule(CalendarFixtures.December10));

        var day3 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, calendar)).Find("#december-3");

        Assert.Empty(day3.QuerySelectorAll("h2 a"));
        Assert.Equal("go/2017/3/1?via=calendar", Assert.Single(day3.QuerySelectorAll("a")).GetAttribute("href"));
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
    public void MarksTheSignedInContributorsSpotsAndRequests()
    {
        var calendar = CalendarFixtures.Calendar(ClaimsOpened) with
        {
            Marks = new CalendarMarks(new HashSet<(int, int)> { (11, 1) }, new HashSet<(int, int)> { (11, 2) }),
        };

        var day11 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, calendar)).Find("#december-11");
        var (mine, requested) = (day11.QuerySelectorAll("ul > li")[0], day11.QuerySelectorAll("ul > li")[1]);

        Assert.Equal("Your spot", mine.QuerySelector("a[href='my-spots']")!.TextContent);
        Assert.Contains("Claimed by Carol", mine.TextContent);
        Assert.Contains("You've requested this spot.", requested.TextContent);
        Assert.Empty(requested.QuerySelectorAll("a[href^='claim/']"));

        // Being theirs doesn't reveal the post early.
        Assert.DoesNotContain("Not revealed yet", day11.TextContent);
    }

    [Fact]
    public void MarksNothingForEveryoneElse()
    {
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        Assert.DoesNotContain("Your spot", grid.Markup);
        Assert.DoesNotContain("You've requested", grid.Markup);
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

    [Fact]
    public void ARevealedPostWithoutATitleIsNotAnEmptyLink()
    {
        var season = CalendarFixtures.BuildSeason(ClaimsOpened);
        var slot = season.Slots.Single(s => s is { Day: 9, Part: 1 });
        slot.AuthorName = "Dee";
        slot.PostUrl = "https://example.com/dee";
        var calendar = Web.Calendar.SeasonCalendar.From(season, CalendarFixtures.Schedule(CalendarFixtures.December10));

        var day9 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, calendar)).Find("#december-9");

        Assert.Empty(day9.QuerySelectorAll("a[href^='go/']"));
        Assert.Contains("No post", day9.TextContent);
        Assert.Equal(1, calendar.PostCount);
    }

    [Fact]
    public void TheCountdownSaysWhenInEasternTimeForTheScriptToAddTheVisitorsOwn()
    {
        var day11 = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened))).Find("#december-11");

        Assert.Contains("At midnight Eastern", day11.TextContent);
        Assert.Equal("2026-12-11T00:00:00.0000000-05:00", day11.QuerySelector("[data-reveal-local]")!.GetAttribute("data-reveal-local"));
    }

    [Fact]
    public void LabelsAreDarkEnoughToRead()
    {
        // slate-400 is about 2.6:1 on white (#169).
        var grid = Render<CalendarGrid>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(ClaimsOpened)));

        Assert.DoesNotContain("text-slate-400", grid.Markup);
    }
}
