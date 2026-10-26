using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: AssemblyFixture(typeof(CSAdvent.IntegrationTests.AppHostFixture))]

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Starts the AppHost once for the whole test run. The Testing environment gives it a throwaway Postgres
/// container with no data volume, so tests never touch local development data.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    // The first run pulls container images and downloads the EF Core tools, so allow plenty of time.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The GitHub user ID the AppHost makes an organizer in the Testing environment (see AppHost.cs). Don't configure it here
    /// with CreateResourceBuilder(...).WithEnvironment: that makes Aspire drop the web project's launch profile environment.
    /// </summary>
    public const long OrganizerGitHubId = 9001;

    private DistributedApplication? _app;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The AppHost hasn't started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CSAdvent_AppHost>(
            [
                "--environment=Testing",
                // A fixed password, so Aspire doesn't generate one and save it to the AppHost's user secrets. Those
                // are shared with local development, whose Postgres data volume was created with the saved password.
                "--Parameters:postgres-password=integration-tests",
            ],
            timeout.Token);

        // The AppHost relays every resource's console to the test output (as CSAdvent.AppHost.Resources.{resource}), standard
        // output as Information and standard error as Error. Left alone that's thousands of lines of the migrations' SQL,
        // which bury a failing test (#177). Warnings and errors still come through.
        appHost.Services.AddLogging(logging => logging.AddFilter("CSAdvent.AppHost.Resources", LogLevel.Warning));

        appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await appHost.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("web", timeout.Token);
    }

    public HttpClient CreateWebClient() => App.CreateHttpClient("web");

    /// <summary>A client with its own cookies that doesn't follow redirects, like a fresh browser session.</summary>
    public HttpClient CreateBrowser() =>
        new(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = App.GetEndpoint("web", "http") };

    /// <summary>A context on the database the AppHost created and migrated.</summary>
    public async Task<AdventDbContext> CreateDbContextAsync()
    {
        var connectionString = await App.GetConnectionStringAsync("csadvent");
        var options = new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql(connectionString).Options;
        return new AdventDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
