using System.Text.Json.Nodes;
using CSAdvent.UnitTests.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Content;

public sealed class StructuredDataTests
{
    private const string Site = "https://csadvent.christmas/";

    private static readonly DateTimeOffset Revealed = new(2026, 12, 6, 0, 0, 0, TimeSpan.FromHours(-5));

    private static EntryView Entry(bool isRevealed = true) => isRevealed
        ? new(1, 2026, 6, 2, 2, true, Revealed, "Spans </script> & you", "https://example.com/spans", "Ada Lovelace", "https://ada.dev", [], [], IsSeasonOver: false)
        : new(1, 2026, 6, 2, 2, false, Revealed, null, null, null, null, [], [], IsSeasonOver: false);

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
    public void ARevealedEntryIsTheAuthorsPostDescribedByItsPageHere()
    {
        var post = StructuredData.Entry(Entry(), Site, "By Ada Lovelace.", "cards/2026/6/2.jpg?v=1");

        Assert.Equal("BlogPosting", (string?)post["@type"]);
        Assert.Equal("Spans </script> & you", (string?)post["headline"]);
        // The author's own post is the original; our page is about it.
        Assert.Equal("https://example.com/spans", (string?)post["url"]);
        Assert.Equal("https://csadvent.christmas/2026/day/6/2", (string?)post["mainEntityOfPage"]);
        Assert.Equal("2026-12-06T00:00:00-05:00", (string?)post["datePublished"]);
        Assert.Equal("Ada Lovelace", (string?)post["author"]!["name"]);
        Assert.Equal("https://ada.dev", (string?)post["author"]!["url"]);
        Assert.Equal("https://csadvent.christmas/cards/2026/6/2.jpg?v=1", (string?)post["image"]);
        Assert.Equal("C# Advent 2026", (string?)post["isPartOf"]!["name"]);
    }

    [Fact]
    public void NothingDescribesAnEntryBeforeItsReveal() =>
        Assert.Throws<ArgumentException>(() => StructuredData.Entry(Entry(isRevealed: false), Site, "", "images/social-card.jpg"));

    [Fact]
    public void TheJsonCantCloseItsScriptElement()
    {
        var json = StructuredData.Serialize(StructuredData.Entry(Entry(), Site, "<b>", "cards/2026/6/2.jpg"));

        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        Assert.Equal("Spans </script> & you", (string?)JsonNode.Parse(json)!["headline"]);
    }
}
