using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;

[assembly: AssemblyFixture(typeof(CSAdvent.IntegrationTests.AppHostFixture))]

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Starts the AppHost once for the whole test run. The Testing environment gives it a throwaway Postgres
/// container with no data volume, so tests never touch local development data.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    /// <summary>
    /// How long the AppHost gets to start, in all. The first run pulls container images and downloads the EF Core tools, and
    /// on a busy machine (several test runs and builds at once) the migrations step alone, which starts dotnet-ef and has it
    /// evaluate the web project with MSBuild before it applies anything, has taken minutes. A quiet machine takes under two.
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(12);

    /// <summary>
    /// How long the first attempt gets before it's abandoned for a fresh one. An overloaded machine has left DCP unable to
    /// update the web app's status ("Executable status update failed ... context deadline exceeded") for the rest of the
    /// run, so waiting longer for the same attempt doesn't help; starting again does.
    /// </summary>
    private static readonly TimeSpan FirstAttemptTimeout = TimeSpan.FromMinutes(7);

    /// <summary>How many times to try starting the AppHost, within <see cref="StartupTimeout"/>.</summary>
    private const int StartAttempts = 2;

    /// <summary>The name of the tests' own HTTP client for the web app (<see cref="CreateWebClient"/>).</summary>
    private const string WebClientName = "tests-web";

    /// <summary>
    /// The GitHub user ID the AppHost makes an organizer in the Testing environment (see AppHost.cs). Don't configure it here
    /// with CreateResourceBuilder(...).WithEnvironment: that makes Aspire drop the web project's launch profile environment.
    /// </summary>
    public const long OrganizerGitHubId = 9001;

    /// <summary>Facebook's link previews, which (like every link preview, and the organizers) get a revealed entry's page.</summary>
    public const string LinkPreviewAgent = "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)";

    private DistributedApplication? _app;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The AppHost hasn't started.");

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        for (var attempt = 1; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            if (attempt < StartAttempts)
            {
                attemptTimeout.CancelAfter(FirstAttemptTimeout);
            }

            try
            {
                _app = await StartAsync(attemptTimeout.Token);
                return;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // Say what was still starting, which a bare cancellation doesn't.
                throw new TimeoutException($"The AppHost didn't start in {StartupTimeout.TotalMinutes:N0} minutes. {DescribeResources(_app)}");
            }
            catch (Exception exception) when (attempt < StartAttempts)
            {
                // On a busy machine Docker can be slow to answer DCP's health checks, which marks the containers
                // "RuntimeUnhealthy" and stops everything waiting for them, or DCP itself can stop keeping up, though nothing's
                // wrong with this run. Start again from scratch, within the same time limit. Stopping the first attempt can
                // hang as well, so it gets a minute, and whatever it leaves is cleaned up when the test run ends.
                var what = exception is OperationCanceledException ? $"in {FirstAttemptTimeout.TotalMinutes:N0} minutes" : $"({exception.Message})";
                TestContext.Current.SendDiagnosticMessage($"The AppHost didn't start {what}. {DescribeResources(_app)} Starting it again.");
                if (_app is { } failed)
                {
                    _app = null;
                    try
                    {
                        await failed.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
                    }
                    catch (Exception stopping)
                    {
                        TestContext.Current.SendDiagnosticMessage($"Stopping the first attempt didn't finish ({stopping.Message}).");
                    }
                }
            }
        }
    }

    /// <summary>Builds and starts the AppHost, and waits for the web app to be healthy.</summary>
    private async Task<DistributedApplication> StartAsync(CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.CSAdvent_AppHost>(
            [
                "--environment=Testing",
                // A fixed password, so Aspire doesn't generate one and save it to the AppHost's user secrets. Those
                // are shared with local development, whose Postgres data volume was created with the saved password.
                "--Parameters:postgres-password=integration-tests",
            ],
            cancellationToken);

        // The AppHost relays every resource's console to the test output (as CSAdvent.AppHost.Resources.{resource}), standard
        // output as Information and standard error as Error. Left alone that's thousands of lines of the migrations' SQL,
        // which bury a failing test (#177). Warnings and errors still come through. Each request through CreateWebClient
        // logs a few lines too (the HTTP client's and Polly's), which say nothing a test needs.
        appHost.Services.AddLogging(logging => logging
            .AddFilter("CSAdvent.AppHost.Resources", LogLevel.Warning)
            .AddFilter("Polly", LogLevel.Warning)
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning));

        // The tests' client tries a GET again after a passing failure. Never a POST: the development endpoints' runs aren't
        // idempotent in what they return, and one that took longer than a timeout on a busy machine may well have finished,
        // so a second try would count nothing and fail the test (#198). Only the tests' client is configured, not every
        // client in the AppHost, whose own health checks needn't wait out retries.
        appHost.Services.AddHttpClient(WebClientName).AddResilienceHandler("tests", pipeline =>
        {
            var retry = new HttpRetryStrategyOptions { MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true };
            retry.DisableForUnsafeHttpMethods();
            pipeline.AddRetry(retry).AddTimeout(TimeSpan.FromMinutes(3));
        });

        // Kept in _app as soon as it's built, so a failure can say where each resource got to, and dispose of it.
        var app = _app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("web", cancellationToken);
        return app;
    }

    /// <summary>
    /// A client that follows redirects. Never point it at a revealed entry's page: that redirects to the author's real post,
    /// on the internet. Read those pages with <see cref="CreateLinkPreview"/>.
    /// </summary>
    public HttpClient CreateWebClient()
    {
        var client = App.Services.GetRequiredService<IHttpClientFactory>().CreateClient(WebClientName);
        client.BaseAddress = App.GetEndpoint("web", "http");

        // The resilience pipeline's own timeout applies instead of the client's 100 seconds.
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>A client with its own cookies that doesn't follow redirects, like a fresh browser session.</summary>
    public HttpClient CreateBrowser() =>
        new(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = App.GetEndpoint("web", "http") };

    /// <summary>
    /// A link preview fetching pages for their cards: the only visitor besides the organizers who gets a revealed entry's page,
    /// where readers and crawlers go straight to the post. It doesn't follow redirects either, so it never reaches a real post.
    /// </summary>
    public HttpClient CreateLinkPreview()
    {
        var client = CreateBrowser();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", LinkPreviewAgent);
        return client;
    }

    /// <summary>A context on the database the AppHost created and migrated.</summary>
    public async Task<AdventDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql(await GetConnectionStringAsync()).Options;
        return new AdventDbContext(options);
    }

    /// <summary>
    /// The connection string for the database the AppHost created, with more time to connect and to run a command than
    /// Npgsql's 15 and 30 seconds. With several test runs on one machine, connecting through Docker's forwarded port has
    /// taken longer than 15 seconds, failing a dozen tests at once (#198).
    /// </summary>
    public async Task<string> GetConnectionStringAsync() =>
        new NpgsqlConnectionStringBuilder(await App.GetConnectionStringAsync("csadvent")) { Timeout = 60, CommandTimeout = 120 }.ConnectionString;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Each resource and where it got to, such as "migrations: Running" or "web: Running (Unhealthy)".</summary>
    private static string DescribeResources(DistributedApplication? app)
    {
        if (app is null)
        {
            return "It wasn't built.";
        }

        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        return string.Join("; ", resources.Select(resource => app.ResourceNotifications.TryGetCurrentState(resource.Name, out var current)
            ? $"{resource.Name}: {current.Snapshot.State?.Text ?? "no state"}{(current.Snapshot.HealthStatus is { } health ? $" ({health})" : "")}"
            : $"{resource.Name}: no state"));
    }
}
