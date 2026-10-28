using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Snapshots;

/// <summary>
/// The Wayback Machine snapshots (#261), every 15 minutes: asks how the captures in progress went, then asks for a few more
/// (<see cref="SnapshotOptions.CapturesPerRun"/>). Not when the app starts, so a deploy or a restart never sends a burst. The
/// AppHost turns it off in the Testing environment; the tests run it on the stand-in, through the development endpoint.
/// </summary>
public sealed partial class SnapshotWorker : PeriodicWorker
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    public static readonly JobStep Snapshots = new("snapshots", "Wayback Machine snapshots", Every);

    private readonly ILogger<SnapshotWorker> _logger;

    public SnapshotWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<SnapshotWorker> logger)
        : base(scopeFactory, runs, timeProvider, logger, Every)
    {
        _logger = logger;
        Steps = [new(Snapshots, SaveAsync)];
    }

    protected override IReadOnlyList<PeriodicStep> Steps { get; }

    protected override bool RunsAtStart => false;

    private async Task SaveAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<SnapshotService>().RunAsync(now, cancellationToken: cancellationToken);
        if (run != SnapshotRun.None)
        {
            LogRun(run.Asked, run.Saved, run.GaveUp);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wayback Machine snapshots: {Asked} asked for, {Saved} saved, {GaveUp} given up on.")]
    private partial void LogRun(int asked, int saved, int gaveUp);
}
