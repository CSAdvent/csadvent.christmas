using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.DeadLinks;

/// <summary>
/// The dead-link check (#201), every 15 minutes: a few posts a run (<see cref="LinkCheckOptions.PostsPerRun"/>), so the whole
/// archive takes a few days and each post comes round about once a month. Not when the app starts, so a deploy or a restart
/// never sends a burst of checks to authors' sites. The AppHost turns it off in the Testing environment, so the tests never
/// reach real sites.
/// </summary>
public sealed partial class LinkCheckWorker : PeriodicWorker
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    public static readonly JobStep DeadLinks = new("dead-links", "Dead-link check", Every);

    private readonly ILogger<LinkCheckWorker> _logger;

    public LinkCheckWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<LinkCheckWorker> logger)
        : base(scopeFactory, runs, timeProvider, logger, Every)
    {
        _logger = logger;
        Steps = [new(DeadLinks, CheckAsync)];
    }

    protected override IReadOnlyList<PeriodicStep> Steps { get; }

    protected override bool RunsAtStart => false;

    private async Task CheckAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = await services.GetRequiredService<LinkCheckService>().RunAsync(now, cancellationToken: cancellationToken);
        if (run.Checked > 0 || run.FoundCopies > 0)
        {
            LogRun(run.Checked, run.FoundDead, run.FoundCopies);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dead-link check: {Checked} posts checked, {FoundDead} found dead, {FoundCopies} archived copies found.")]
    private partial void LogRun(int @checked, int foundDead, int foundCopies);
}
