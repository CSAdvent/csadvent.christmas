using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Auth;

/// <summary>
/// Reloads <see cref="Organizers"/> when the app starts and every 15 seconds, so an organizer invited or removed on one
/// replica is one (or isn't) on the others too. It's one small query, and the replica that made the change reloads straight
/// away.
/// </summary>
public sealed class OrganizersRefresher(Organizers organizers, IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<OrganizersRefresher> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    public static readonly JobStep Refresh = new("organizers", "Loading the organizers", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(Refresh, (_, _, cancellationToken) => organizers.RefreshAsync(cancellationToken)),
    ];

    /// <summary>
    /// A warning: it runs every 15 seconds, so a passing database hiccup would trip the error alert, and the list it had stays
    /// in use. A step that keeps failing still turns <c>/health</c> Degraded.
    /// </summary>
    protected override LogLevel FailureLevel => LogLevel.Warning;
}
