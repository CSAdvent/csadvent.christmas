using System.Xml.Linq;
using CSAdvent.Web.Content;

namespace CSAdvent.UnitTests.Content;

public sealed class SearchTests
{
    [Fact]
    public void RobotsKeepsSearchEnginesOutOfPrivatePagesAndPointsAtTheSitemap()
    {
        var robots = SearchEndpoints.Robots("https://csadvent.christmas/");

        Assert.StartsWith("User-agent: *\n", robots);
        Assert.Contains("Disallow: /manage\n", robots);
        Assert.Contains("Disallow: /random\n", robots);
        // Crawlers may follow the tracked redirect to the authors' posts (#167); it answers with X-Robots-Tag: noindex.
        Assert.DoesNotContain("/go", robots);
        Assert.EndsWith("\nSitemap: https://csadvent.christmas/sitemap.xml\n", robots);
    }

    [Fact]
    public void TheSitemapListsAbsoluteAddressesWithTheirDates()
    {
        var xml = SearchEndpoints.Sitemap("https://csadvent.christmas", [new("", null), new("2025/day/6/2", new DateOnly(2025, 12, 6))]);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = XDocument.Parse(xml).Root!.Elements(ns + "url").ToList();

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Equal(["https://csadvent.christmas/", "https://csadvent.christmas/2025/day/6/2"], urls.Select(u => u.Element(ns + "loc")!.Value));
        Assert.Null(urls[0].Element(ns + "lastmod"));
        Assert.Equal("2025-12-06", urls[1].Element(ns + "lastmod")!.Value);
    }
}
