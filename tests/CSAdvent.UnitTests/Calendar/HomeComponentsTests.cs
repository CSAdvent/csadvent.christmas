using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Home;
using Microsoft.AspNetCore.Components;

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
    public void BetweenSeasonsItLeadsWithTheNextSeason()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, PostCount: 50, AuthorCount: 47))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, new DateOnly(2026, 11, 1), DaysUntilSignUps: null))
            .Add(c => c.Message, "See you **next December**!"));

        // The next season first (#200): when its sign-ups open, what C# Advent is, and how to take part.
        Assert.Equal("C# Advent 2026", page.Find("h1").TextContent);
        Assert.Equal("Sign-ups open on November 1.", page.Find("[role=status]").TextContent);
        Assert.Contains("Two new C# posts every day from December 1 to 25", page.Find("h1 + p").TextContent);
        Assert.Equal(["How to take part", "Thanks for C# Advent 2025!", "Wrote for C# Advent before?"], page.FindAll("h2").Select(h => h.TextContent));
        Assert.Equal(3, page.FindAll("#take-part ~ ol li").Count);

        // Then the thanks for the last season, with the organizers' message.
        var thanks = page.Find("section[aria-labelledby=thanks]");
        Assert.Contains("50 posts from 47 authors", thanks.TextContent);
        Assert.Contains("<strong>next December</strong>", thanks.InnerHtml);

        Assert.Equal(
            ["calendar/2026/sign-ups.ics", "calendar/2026/starts.ics", "about", "terms", "archive/2025", "archive", OffSeason.SignInToMySpots, "rss", "others"],
            page.FindAll("a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void TheThanksLinkToAPublishedRetrospective()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47, HasRetrospective: true))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, new DateOnly(2026, 11, 1), DaysUntilSignUps: null)));

        var link = page.Find("section[aria-labelledby=thanks] a[href='archive/2025/retrospective']");
        Assert.Equal("Look back at 2025", link.TextContent);
    }

    [Fact]
    public void WithoutARetrospectiveTheThanksOnlyLinkTheArchive()
    {
        var page = Render<OffSeason>(p => p.Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47)));

        Assert.Empty(page.FindAll("a[href$='/retrospective']"));
    }

    [Fact]
    public void PastAuthorsAreAskedToSignInForTheirHistory()
    {
        var signedOut = Render<OffSeason>(p => p.Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47)));
        var invitation = signedOut.Find("section[aria-labelledby=past-authors]");
        Assert.Contains("Sign in with GitHub to see your C# Advent history and how your posts did.", System.Text.RegularExpressions.Regex.Replace(invitation.TextContent, @"\s+", " "));
        Assert.Equal("signin?returnUrl=%2Fmy-spots", invitation.QuerySelector("a")!.GetAttribute("href"));

        // Once they're signed in, it's straight to My spots.
        var signedIn = Render<OffSeason>(p => p.Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47)).Add(c => c.IsSignedIn, true));
        Assert.Equal("my-spots", signedIn.Find("section[aria-labelledby=past-authors] a").GetAttribute("href"));
    }

    [Fact]
    public void TheSubscribeBoxComesAfterHowToTakePart()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47))
            .Add(c => c.Subscribe, (RenderFragment)(builder => builder.AddMarkupContent(0, "<div id=\"announcements\">Subscribe</div>"))));

        var order = page.FindAll("#take-part, #announcements, #thanks").Select(e => e.Id);
        Assert.Equal(["take-part", "announcements", "thanks"], order);
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
        Assert.Contains("Sign-ups open on November 1.", status.TextContent);
        Assert.Equal("C# Advent 2026", page.Find("h1").TextContent);
    }

    [Fact]
    public void BeforeTheNextSeasonIsScheduledItPromisesAnAnnouncement()
    {
        var page = Render<OffSeason>(p => p
            .Add(c => c.LastSeason, new FinishedSeason(2025, 50, 47))
            .Add(c => c.NextSeason, new UpcomingSeason(2026, SignUpsOpenOn: null, DaysUntilSignUps: null)));

        Assert.Equal("C# Advent 2026", page.Find("h1").TextContent);
        Assert.Equal("We'll announce when sign-ups open, here and on social media.", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void BeforeTheNextSeasonExistsItIsTheOneAfterTheLast()
    {
        var page = Render<OffSeason>(p => p.Add(c => c.LastSeason, new FinishedSeason(2026, 50, 47)));

        Assert.Equal("C# Advent 2027", page.Find("h1").TextContent);
        Assert.Equal("We'll announce when sign-ups open, here and on social media.", page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void WithoutAnySeasonItIntroducesCSharpAdvent()
    {
        var page = Render<OffSeason>();

        Assert.Equal("C# Advent", page.Find("h1").TextContent);
        Assert.Equal("We'll announce the next C# Advent here and on social media.", page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("#thanks"));
        Assert.Equal(["about", "terms", "archive", OffSeason.SignInToMySpots, "rss", "others"], page.FindAll("a").Select(a => a.GetAttribute("href")));
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
