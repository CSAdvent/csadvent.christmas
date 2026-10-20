using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private DistributedApplication? _app;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The AppHost hasn't started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CSAdvent_AppHost>(
            ["--environment=Testing"],
            timeout.Token);

        appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        _app = await appHost.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("web", timeout.Token);
    }

    public HttpClient CreateWebClient() => App.CreateHttpClient("web");

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
