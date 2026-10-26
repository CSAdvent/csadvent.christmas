using CSAdvent.Web.Entries;
using Microsoft.AspNetCore.Http;

namespace CSAdvent.UnitTests.Entries;

public sealed class EntryTrafficTests
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

    [Theory]
    [InlineData("bluesky", "bluesky")]
    [InlineData(" RSS ", "rss")]
    [InlineData("myspace", "other")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ViaTagsAreNormalized(string? via, string? expected) => Assert.Equal(expected, EntryVia.Normalize(via));

    [Theory]
    [InlineData(Browser, true)]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", false)]
    [InlineData("LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)", false)]
    [InlineData("Mastodon/4.3.0 (http.rb/5.2.0; +https://hachyderm.io/)", false)]
    [InlineData("Bluesky Cardyb/1.1", false)]
    [InlineData("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)", false)]
    [InlineData("curl/8.9.1", false)]
    [InlineData("", false)]
    public void OnlyPeopleAreCounted(string userAgent, bool counted)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Headers.UserAgent = userAgent;

        Assert.Equal(counted, EntryTraffic.IsPerson(context.Request));
    }

    [Fact]
    public void PrefetchesAndOtherMethodsAreNotCounted()
    {
        var prefetch = new DefaultHttpContext();
        prefetch.Request.Method = "GET";
        prefetch.Request.Headers.UserAgent = Browser;
        prefetch.Request.Headers["Sec-Purpose"] = "prefetch;prerender";

        var head = new DefaultHttpContext();
        head.Request.Method = "HEAD";
        head.Request.Headers.UserAgent = Browser;

        Assert.False(EntryTraffic.IsPerson(prefetch.Request));
        Assert.False(EntryTraffic.IsPerson(head.Request));
    }

    [Fact]
    public void AClickIsCountedOnceEvenFromBlazorsEnhancedNavigation()
    {
        // Blazor's enhanced navigation fetches the link, then loads it again as a whole page (#150): only the page counts.
        var fetch = new DefaultHttpContext();
        fetch.Request.Method = "GET";
        fetch.Request.Headers.UserAgent = Browser;
        fetch.Request.Headers.Accept = "text/html; blazor-enhanced-nav=on";

        var page = new DefaultHttpContext();
        page.Request.Method = "GET";
        page.Request.Headers.UserAgent = Browser;
        page.Request.Headers.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

        Assert.False(EntryTraffic.IsClick(fetch.Request));
        Assert.True(EntryTraffic.IsClick(page.Request));

        // A visit to an entry page is one request either way, so it still counts.
        Assert.True(EntryTraffic.IsPerson(fetch.Request));
    }

    [Fact]
    public void LinksCarryTheirTag()
    {
        Assert.Equal("2026/day/3/2?via=bluesky", EntryLinks.Page(2026, 3, 2, "bluesky"));
        Assert.Equal("2026/day/3/2", EntryLinks.Page(2026, 3, 2));
        Assert.Equal("https://csadvent.christmas/go/2026/3/2?via=email", EntryLinks.Absolute("https://csadvent.christmas/", EntryLinks.Go(2026, 3, 2, "email")));
    }
}
