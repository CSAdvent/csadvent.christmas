namespace CSAdvent.IntegrationTests;

public sealed class WebTests(AppHostFixture fixture)
{
    [Fact]
    public async Task HomePageRenders()
    {
        using var client = fixture.CreateWebClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("C# Advent", html);
    }

    [Fact]
    public async Task EveryPageLinksToThePrivacyAndTermsPages()
    {
        using var client = fixture.CreateWebClient();

        var html = await client.GetStringAsync("/archive", TestContext.Current.CancellationToken);

        Assert.Contains("<a href=\"privacy\"", html);
        Assert.Contains("<a href=\"terms\"", html);

        // Follow links for every account, with rel="me" so the Mastodon profile can verify the site.
        Assert.Contains("href=\"https://bsky.app/profile/csadvent.christmas\"", html);
        Assert.Contains("<a href=\"https://mastodon.social/@csadvent\" rel=\"me noopener\"", html);
        Assert.Contains("href=\"https://www.facebook.com/csadvent/\"", html);
        Assert.Contains("href=\"https://www.linkedin.com/company/csadvent\"", html);
        Assert.Contains("href=\"https://www.reddit.com/r/csadvent\"", html);
        Assert.Contains("href=\"https://x.com/csadvent\"", html);
    }

    [Theory]
    [InlineData("/privacy", "on February 1 after each season")]
    [InlineData("/terms", "Make it about C#")]
    public async Task PrivacyAndTermsPagesShowHowToReachTheOrganizers(string path, string expected)
    {
        using var client = fixture.CreateWebClient();

        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);

        // Site:ContactEmail, from appsettings.json.
        Assert.Contains("href=\"mailto:hello@csadvent.christmas\"", html);
        Assert.Contains(expected, html);
    }

    [Fact]
    public async Task TheEmailPreviewsListAndRenderEveryEmail()
    {
        using var client = fixture.CreateWebClient();

        var index = await client.GetStringAsync("/dev/emails", TestContext.Current.CancellationToken);
        Assert.Contains("Request received", index);
        Assert.Contains("Request approved", index);

        var html = await client.GetStringAsync("/dev/emails/claim-approved", TestContext.Current.CancellationToken);
        Assert.Contains("You&#x27;re in, Ada Lovelace!", html);

        var text = await client.GetAsync("/dev/emails/claim-approved/text", TestContext.Current.CancellationToken);
        Assert.Equal("text/plain", text.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("You're in, Ada Lovelace!", await text.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var missing = await client.GetAsync("/dev/emails/no-such-email", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task WwwRedirectsToTheCanonicalHostKeepingThePath()
    {
        using var browser = fixture.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/archive/2017?via=bluesky");
        request.Headers.Host = "www.localhost";

        var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        // Development's Site:BaseUrl is https://localhost:7092, so its canonical host is localhost.
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://localhost/archive/2017?via=bluesky", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task HealthChecksAnswerBeforeAnythingCanRedirectThem(string path)
    {
        // On the www host every other page redirects, which a probe would count as healthy.
        using var browser = fixture.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = "www.localhost";

        var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchEnginesGetRobotsAndASitemapOfThePublicPages()
    {
        using var client = fixture.CreateWebClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var robots = await client.GetStringAsync("/robots.txt", cancellationToken);
        Assert.Contains("Disallow: /manage", robots);
        Assert.Contains("Sitemap: https://localhost:7092/sitemap.xml", robots);

        var sitemap = await client.GetStringAsync("/sitemap.xml", cancellationToken);
        Assert.Contains("<loc>https://localhost:7092/archive/2017</loc>", sitemap);
        Assert.Contains("<loc>https://localhost:7092/authors</loc>", sitemap);
        // Not entry pages, which send crawlers on to the authors' posts.
        Assert.DoesNotContain("/day/", sitemap);
        Assert.Contains("<loc>https://localhost:7092/about</loc>", sitemap);

        // Never the test season, and never a season that isn't over (the tests' far-future ones).
        Assert.DoesNotContain("/9000", sitemap);
        Assert.DoesNotContain("archive/21", sitemap);
    }

    [Fact]
    public async Task LegacyAddressesStillWork()
    {
        // /substitute needs a current season. Other tests make some too, but this one mustn't depend on them running first.
        await TestSeasons.EnsureAsync(fixture, 2420, claimsOpen: true);
        using var browser = fixture.CreateBrowser();

        var contributor = await browser.GetAsync("/contributor", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MovedPermanently, contributor.StatusCode);
        Assert.Equal("/my-spots", contributor.Headers.Location!.OriginalString);

        var substitute = await browser.GetAsync("/substitute", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, substitute.StatusCode);
        Assert.StartsWith("/backups/", substitute.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task UnknownPageReturnsNotFound()
    {
        using var client = fixture.CreateWebClient();

        var response = await client.GetAsync("/this-page-does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheckPassesWithTheDatabaseConnected()
    {
        using var client = fixture.CreateWebClient();

        // The health endpoint includes the DbContext check that the Aspire Npgsql integration registers.
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
