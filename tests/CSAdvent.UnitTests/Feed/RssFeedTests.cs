using System.Xml.Linq;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Feed;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Feed;

public sealed class RssFeedTests
{
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private static RssFeed CreateFeed() => new(
        null!,
        new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System),
        Options.Create(new SiteOptions { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" }));

    private static readonly FeedItem[] Items =
    [
        new(2026, 2, 1, 2, "Records & you", "Ada Lovelace", new DateTimeOffset(2026, 12, 2, 5, 0, 0, TimeSpan.Zero)),
        new(2026, 2, 2, 2, "Spans", "Grace Hopper", new DateTimeOffset(2026, 12, 2, 5, 0, 0, TimeSpan.Zero)),
        new(2017, 1, 1, 1, "The first one", "Matt Groves", new DateTimeOffset(2017, 12, 1, 5, 0, 0, TimeSpan.Zero)),
    ];

    [Fact]
    public void WritesValidRss20WithAChannelThatLinksToTheSite()
    {
        var rss = XDocument.Parse(CreateFeed().Write(Items));

        Assert.Equal("2.0", rss.Root!.Attribute("version")!.Value);
        var channel = rss.Root.Element("channel")!;
        Assert.Equal("C# Advent", channel.Element("title")!.Value);
        Assert.Equal("https://csadvent.christmas", channel.Element("link")!.Value);
        Assert.NotEmpty(channel.Element("description")!.Value);
        Assert.Equal("Wed, 02 Dec 2026 05:00:00 GMT", channel.Element("lastBuildDate")!.Value);
        Assert.Equal("https://csadvent.christmas/rss", channel.Element(Atom + "link")!.Attribute("href")!.Value);
    }

    [Fact]
    public void ItemsLinkThroughUsWithStableGuidsAndTheirRevealDate()
    {
        var items = XDocument.Parse(CreateFeed().Write(Items)).Root!.Element("channel")!.Elements("item").ToList();

        Assert.Equal(["Records & you", "Spans", "The first one"], items.Select(i => i.Element("title")!.Value));
        var first = items[0];
        Assert.Equal("https://csadvent.christmas/2026/day/2/1?via=rss", first.Element("link")!.Value);
        Assert.Equal("Day 2, post 1 of C# Advent 2026, by Ada Lovelace.", first.Element("description")!.Value);
        Assert.Equal("Ada Lovelace", first.Element(Dc + "creator")!.Value);
        Assert.Equal("Wed, 02 Dec 2026 05:00:00 GMT", first.Element("pubDate")!.Value);
        Assert.Equal("csadvent-2026-2-1", first.Element("guid")!.Value);
        Assert.Equal("false", first.Element("guid")!.Attribute("isPermaLink")!.Value);
        Assert.Equal("Day 1 of C# Advent 2017, by Matt Groves.", items[2].Element("description")!.Value);
    }

    [Fact]
    public void ASeasonsFeedLinksToItsArchivePage()
    {
        var channel = XDocument.Parse(CreateFeed().Write(Items[2..], 2017)).Root!.Element("channel")!;

        Assert.Equal("C# Advent 2017", channel.Element("title")!.Value);
        Assert.Equal("https://csadvent.christmas/archive/2017", channel.Element("link")!.Value);
        Assert.Equal("https://csadvent.christmas/archive/2017/rss", channel.Element(Atom + "link")!.Attribute("href")!.Value);
    }
}
