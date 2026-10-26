using CSAdvent.Web.Data;

namespace CSAdvent.Web.Jobs;

/// <summary>
/// A background job that runs its steps in order on a timer: when the app starts (unless <see cref="RunsAtStart"/> says
/// otherwise), then every interval.
/// <list type="bullet">
/// <item>Each step gets a scope and a try/catch of its own (#147), so one that throws (a bug, or one bad post) never skips the
/// steps after it, and nothing it left tracked is saved by them.</item>
/// <item>A step that finds a table or column missing logs a warning, not an error for the alert: shortly after a deploy, the
/// migrations job hasn't added it yet, and the next run after the migrations works (#225).</item>
/// <item>Each step's outcome is recorded (<see cref="JobRuns"/>) for /manage and the health check (#172).</item>
/// </list>
/// Nothing but cancellation gets out when the app stops: an unhandled exception would stop the whole app.
/// </summary>
public abstract partial class PeriodicWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger logger, TimeSpan interval)
    : BackgroundService
{
    /// <summary>The steps, in the order they run.</summary>
    protected abstract IReadOnlyList<PeriodicStep> Steps { get; }

    /// <summary>Whether the first run is when the app starts, rather than one interval later.</summary>
    protected virtual bool RunsAtStart => true;

    /// <summary>How a failed step is logged, unless it's the migrations it's waiting for. Errors trip the error alert.</summary>
    protected virtual LogLevel FailureLevel => LogLevel.Error;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var step in Steps)
        {
            runs.Started(step.Step);
        }

        using var timer = new PeriodicTimer(interval, timeProvider);
        if (RunsAtStart)
        {
            await RunOnceAsync(stoppingToken);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>Runs every step once, in order, whatever happens to the others. Tests use it to run a pass without the timer.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        foreach (var step in Steps)
        {
            await RunStepAsync(step, cancellationToken);
        }
    }

    private async Task RunStepAsync(PeriodicStep step, CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await step.RunAsync(scope.ServiceProvider, timeProvider.GetUtcNow(), cancellationToken);
            succeeded = true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
        {
            LogNotMigrated(exception, step.Step.Name, interval);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(FailureLevel, exception, step.Step.Name, interval);
        }

        await runs.RecordAsync(step.Step, succeeded, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Step} couldn't run yet: the database isn't migrated to this version. Trying again in {Interval}")]
    private partial void LogNotMigrated(Exception exception, string step, TimeSpan interval);

    [LoggerMessage(Message = "{Step} failed. Trying again in {Interval}")]
    private partial void LogFailed(LogLevel level, Exception exception, string step, TimeSpan interval);
}
