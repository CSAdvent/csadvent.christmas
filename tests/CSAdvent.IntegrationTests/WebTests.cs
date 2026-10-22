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
