using System.Text.Json.Nodes;
using CSAdvent.UnitTests.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Content;

public sealed class StructuredDataTests
{
    private const string Site = "https://csadvent.christmas/";

    [Fact]
    public void TheSiteIsPublishedByCSharpAdventWithItsAccounts()
    {
        var accounts = new SocialAccountsOptions { Bluesky = "csadvent.christmas", Mastodon = "@csadvent@mastodon.social" };

        var site = StructuredData.WebSite(Site, accounts);

        Assert.Equal("WebSite", (string?)site["@type"]);
        Assert.Equal("https://csadvent.christmas/", (string?)site["url"]);
        Assert.Equal("C# Advent", (string?)site["publisher"]!["name"]);
        Assert.Equal(
            ["https://bsky.app/profile/csadvent.christmas", "https://mastodon.social/@csadvent"],
            site["publisher"]!["sameAs"]!.AsArray().Select(url => (string?)url));
    }

    [Fact]
    public void ASeasonIsAnOnlineEventFromItsFirstRevealToTheEndOfDecember25()
    {
        var schedule = CalendarFixtures.Schedule(CalendarFixtures.December10);

        var season = StructuredData.SeasonEvent(2025, schedule, Site, "archive/2025", "cards/2025.jpg?v=50");

        Assert.Equal("Event", (string?)season["@type"]);
        Assert.Equal("C# Advent 2025", (string?)season["name"]);
        Assert.Equal("2025-12-01T00:00:00-05:00", (string?)season["startDate"]);
        Assert.Equal("2025-12-26T00:00:00-05:00", (string?)season["endDate"]);
        Assert.Equal("https://schema.org/OnlineEventAttendanceMode", (string?)season["eventAttendanceMode"]);
        Assert.Equal("https://csadvent.christmas/archive/2025", (string?)season["location"]!["url"]);
        Assert.Equal("https://csadvent.christmas/cards/2025.jpg?v=50", (string?)season["image"]![0]);
    }

    [Fact]
    public void TheJsonCantCloseItsScriptElement()
    {
        var json = StructuredData.Serialize(new JsonObject { ["headline"] = "Spans </script> & you" });

        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        Assert.Equal("Spans </script> & you", (string?)JsonNode.Parse(json)!["headline"]);
    }
}
