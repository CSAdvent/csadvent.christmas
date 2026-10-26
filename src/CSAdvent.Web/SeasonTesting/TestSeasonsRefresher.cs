using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// Reloads <see cref="TestSeasons"/> when the app starts and every 15 seconds, so a change made on one replica reaches the
/// others. It's one small query, and the replica that made the change reloads straight away.
/// </summary>
public sealed class TestSeasonsRefresher(TestSeasons testSeasons, IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<TestSeasonsRefresher> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    public static readonly JobStep Refresh = new("test-seasons", "Loading the test season", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(Refresh, (_, _, cancellationToken) => testSeasons.RefreshAsync(cancellationToken)),
    ];

    /// <summary>
    /// A warning, as it always was: it runs every 15 seconds, so a passing database hiccup would trip the error alert, and it
    /// only matters to a rehearsal. A step that keeps failing still turns <c>/health</c> Degraded.
    /// </summary>
    protected override LogLevel FailureLevel => LogLevel.Warning;
}
