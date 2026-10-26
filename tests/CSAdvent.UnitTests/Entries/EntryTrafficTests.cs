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

        // It's a person's request, just not the one that counts.
        Assert.True(EntryTraffic.IsPerson(fetch.Request));
    }

    [Theory]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)")]
    [InlineData("Facebot")]
    [InlineData("Twitterbot/1.0")]
    [InlineData("LinkedInBot/1.0 (compatible; Mozilla/5.0; Apache-HttpClient +http://www.linkedin.com)")]
    [InlineData("http.rb/5.2.0 (Mastodon/4.3.0; +https://mastodon.social/)")]
    [InlineData("Mozilla/5.0 (compatible; Bluesky Cardyb/1.1; +mailto:support@bsky.app)")]
    [InlineData("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)")]
    [InlineData("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)")]
    [InlineData("TelegramBot (like TwitterBot)")]
    [InlineData("WhatsApp/2.23.20.0 A")]
    [InlineData("Mozilla/5.0 (compatible; redditbot/1.0; +http://www.reddit.com/feedback)")]
    [InlineData("Iframely/1.3.1 (+https://iframely.com/docs/about)")]
    [InlineData("Mozilla/5.0 (compatible; Embedly/0.2; +http://support.embed.ly/)")]
    [InlineData("Mozilla/5.0 (Windows NT 6.1; WOW64) SkypeUriPreview Preview/0.5 skype-url-preview@microsoft.com")]
    public void LinkPreviewsGetThePageAndAreNeverCounted(string userAgent)
    {
        var request = Request(userAgent);

        Assert.True(EntryTraffic.IsLinkPreview(request));
        Assert.False(EntryTraffic.IsPerson(request));
    }

    [Theory]
    [InlineData(Browser)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm) Chrome/116.0.1938.76 Safari/537.36")]
    // The platforms' own in-app browsers are people, following the link.
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 [FBAN/FBIOS;FBAV/480.0.0.0]")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 [LinkedInApp]/9.30")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36 Telegram-Android/11.2.3")]
    [InlineData("curl/8.9.1")]
    [InlineData("")]
    public void EveryoneElseGoesStraightToThePost(string userAgent) => Assert.False(EntryTraffic.IsLinkPreview(Request(userAgent)));

    [Fact]
    public void NoLinkPreviewIsEverCounted()
    {
        foreach (var marker in EntryTraffic.LinkPreviewAgents)
        {
            Assert.False(EntryTraffic.IsPerson(Request($"Mozilla/5.0 (compatible; {marker}/1.0)")), marker);
        }
    }

    [Fact]
    public void LinksCarryTheirTag()
    {
        Assert.Equal("2026/day/3/2?via=bluesky", EntryLinks.Page(2026, 3, 2, "bluesky"));
        Assert.Equal("2026/day/3/2", EntryLinks.Page(2026, 3, 2));
        Assert.Equal("https://csadvent.christmas/go/2026/3/2?via=email", EntryLinks.Absolute("https://csadvent.christmas/", EntryLinks.Go(2026, 3, 2, "email")));
    }

    private static HttpRequest Request(string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Headers.UserAgent = userAgent;
        return context.Request;
    }
}
