using System.IO.Compression;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// What pages load, and how they're cached (#186) and compressed (#164): no Blazor script, and a page without a form has no
/// antiforgery cookie and can be kept by the browser.
/// </summary>
public sealed class PageDeliveryTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublicPagesLoadNoBlazorScriptAndCanBeKept()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/about", CancellationToken);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);

        // Nothing is interactive, so every link loads a whole page: /go is asked for once (#150), and the countdown starts again
        // on every page (#156).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("_framework/blazor", html);
        Assert.DoesNotContain("importmap", html);
        Assert.DoesNotContain("Blazor-Server-Component-State", html);
        Assert.Contains("<script src=\"js/countdown.", html);

        // No form, so no antiforgery token: no cookie, and the browser may keep the page if it asks again before showing it.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl is { Private: true, NoCache: true, NoStore: false });
        Assert.Equal("SAMEORIGIN", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        var etag = response.Headers.ETag;
        Assert.NotNull(etag);
        Assert.True(etag.IsWeak);

        // Asked again with its ETag, the unchanged page is a 304.
        using var again = new HttpRequestMessage(HttpMethod.Get, "/about");
        again.Headers.IfNoneMatch.Add(etag);
        var notModified = await browser.SendAsync(again, CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync(CancellationToken));
    }

    [Fact]
    public async Task PagesWithAFormAreNeverKept()
    {
        using var browser = fixture.CreateBrowser();

        // The announcements sign-up form has an antiforgery token.
        var response = await browser.GetAsync("/announcements", CancellationToken);

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Null(response.Headers.ETag);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/archive/2017", "br", "C# Advent 2017")]
    [InlineData("/rss", "br", "<rss")]
    [InlineData("/sitemap.xml", "gzip", "<urlset")]
    public async Task PagesFeedsAndTheSitemapAreCompressed(string path, string encoding, string expected)
    {
        using var browser = fixture.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptEncoding.ParseAdd(encoding);

        var response = await browser.SendAsync(request, CancellationToken);

        Assert.Equal([encoding], response.Content.Headers.ContentEncoding);
        await using var body = await response.Content.ReadAsStreamAsync(CancellationToken);
        await using Stream decompressed = encoding == "br" ? new BrotliStream(body, CompressionMode.Decompress) : new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);
        Assert.Contains(expected, await reader.ReadToEndAsync(CancellationToken));
    }

    [Fact]
    public async Task ASignedInVisitorsPagesAreNotCompressed()
    {
        // They carry the visitor's details and the sign-out form's antiforgery token, which BREACH could learn from compressed pages.
        using var browser = await fixture.SignInAsync("delivery-dee", 8421);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/archive/2017");
        request.Headers.AcceptEncoding.ParseAdd("br");

        var response = await browser.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
