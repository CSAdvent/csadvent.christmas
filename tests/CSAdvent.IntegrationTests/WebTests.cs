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
        Assert.Contains("<h1>C# Advent</h1>", html);
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
