using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Home;

namespace CSAdvent.UnitTests.Calendar;

public sealed class HomeComponentsTests : BunitContext
{
    private static readonly DateTimeOffset October20 = new(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset November1 = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void SaysSignUpsOpenSoonUntilTheyAreScheduled()
    {
        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(claimsOpenAt: null, now: October20)));

        Assert.Contains("Sign-ups open soon.", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void SaysWhenSignUpsOpenInTheRevealTimeZone()
    {
        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1, now: October20)));

        Assert.Contains("Sign-ups open on November 1.", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void CountsTheSpotsStillOpen()
    {
        // On November 2 nothing is revealed yet, so every spot except Ada's, Bob's and Carol's can be claimed.
        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1, now: November1.AddDays(1))));

        Assert.Contains("47 of 50 spots are still available", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void SaysThatsAWrapOnceEveryDayIsRevealed()
    {
        var christmas = new DateTimeOffset(2026, 12, 25, 12, 0, 0, TimeSpan.FromHours(-5));

        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1, now: christmas)));

        Assert.Contains("That's a wrap! All 2 posts are below.", page.Find("[role=status]").TextContent);
        Assert.DoesNotContain("backup list", page.Markup);
    }

    [Fact]
    public void BetweenSeasonsThanksEveryoneForTheLastSeason()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, PostCount: 50, AuthorCount: 47))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, new DateOnly(2026, 11, 1), DaysUntilSignUps: null))
            .Add(c => c.Message, "See you **next December**!"));

        Assert.Equal("Thanks for C# Advent 2025!", page.Find("h1").TextContent);
        Assert.Contains("50 posts from 47 authors", page.Markup);
        Assert.Equal("Sign-ups for C# Advent 2026 open on November 1.", page.Find("[role=status]").TextContent);
        Assert.Contains("<strong>next December</strong>", page.Markup);
        Assert.Equal(
            ["calendar/2026/sign-ups.ics", "calendar/2026/starts.ics", "archive/2025", "archive", "rss", "others"],
            page.FindAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void BeforeSignUpsAreScheduledOnlyTheStartGoesInACalendar()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, SignUpsOpenOn: null, DaysUntilSignUps: null)));

        var links = page.FindAll("a[href^='calendar/']");
        Assert.Equal(["calendar/2026/starts.ics"], links.Select(a => a.GetAttribute("href")));
        Assert.Equal("C# Advent 2026 starts", links[0].TextContent);
    }

    [Fact]
    public void TheTestSeasonNeverGoesInACalendar()
    {
        var page = Render<OffSeason>(p => p.Add(c => c.NextSeason, new UpcomingSeason(CSAdvent.Web.Data.Season.TestYear, new DateOnly(2026, 11, 1), null)));

        Assert.Empty(page.FindAll("a[href^='calendar/']"));
    }

    [Theory]
    [InlineData(33, "33 days until sign-ups open!")]
    [InlineData(1, "1 day until sign-ups open!")]
    [InlineData(0, "Sign-ups open today!")]
    public void CountsDownToSignUps(int days, string expected)
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, new DateOnly(2026, 11, 1), days)));

        var status = page.Find("[role=status]");
        Assert.Equal(expected, status.QuerySelector("p")!.TextContent);
        Assert.Contains("Sign-ups for C# Advent 2026 open on November 1.", status.TextContent);
    }

    [Fact]
    public void BeforeTheNextSeasonExistsItPromisesAnAnnouncement()
    {
        var page = Render<OffSeason>(p => p.Add(c => c.LastSeason, new FinishedSeason(2026, 50, 47)));

        Assert.Equal("We'll announce C# Advent 2027 here and on social media.", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void WithoutAnySeasonItIntroducesCSharpAdvent()
    {
        var page = Render<OffSeason>();

        Assert.Equal("C# Advent", page.Find("h1").TextContent);
        Assert.Equal(["archive", "rss", "others"], page.FindAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void InDecemberItJumpsToToday()
    {
        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1)));

        var jump = page.Find("a[href$='#december-10']");
        Assert.Equal("#december-10", jump.GetAttribute("href"));
        Assert.Contains("Jump to today, December 10", jump.TextContent);
        Assert.NotNull(page.Find("#december-10"));
    }

    [Fact]
    public void BeforeDecemberThereIsNoTodayToJumpTo()
    {
        var page = Render<CurrentSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1, now: October20)));

        Assert.DoesNotContain("Jump to today", page.Markup);
    }
}
