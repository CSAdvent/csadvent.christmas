using Bunit;
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
    public void ThanksEveryoneOffSeason()
    {
        var page = Render<OffSeason>(p => p.Add(c => c.Calendar, CalendarFixtures.Calendar(November1, now: new DateTimeOffset(2027, 1, 10, 12, 0, 0, TimeSpan.Zero))));

        Assert.Contains("Thanks for C# Advent 2026!", page.Find("h1").TextContent);
        Assert.Contains("We'll announce sign-ups for C# Advent 2027 here", page.Markup);
        Assert.Equal("archive", page.Find("header a").GetAttribute("href"));
    }
}
