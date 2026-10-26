using System.Collections.Concurrent;
using CSAdvent.Web.Data;

namespace CSAdvent.Web.Jobs;

/// <summary>A step's runs as /manage shows them: on any replica, as far as this one knows.</summary>
public sealed record JobStepStatus(JobStep Step, JobHealth Health, DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt);

/// <summary>Every step, in <see cref="JobSteps.All"/>'s order.</summary>
/// <param name="NotMigrated">
/// <c>job_heartbeats</c> isn't there yet (the deploy's migrations haven't run), so the times are only this replica's.
/// </param>
public sealed record JobStatusReport(IReadOnlyList<JobStepStatus> Steps, bool NotMigrated);

/// <summary>
/// When each background job step last succeeded and failed (#172), recorded by the workers after every run.
/// <list type="bullet">
/// <item>In memory, for this replica: what the health check goes by (<see cref="JobsHealthCheck"/>). Only steps whose worker
/// has started here count, so the steps the Testing environment turns off are never late.</item>
/// <item>In <c>job_heartbeats</c>, for every replica: what /manage shows, so it's the same whichever replica answers and
/// survives a deploy. A step is saved when its outcome changes, and otherwise at most once every <see cref="SaveEvery"/>, so
/// the email queue (every 2 seconds) doesn't write every 2 seconds.</item>
/// </list>
/// Recording never throws: a run that couldn't be saved is still in memory, and saved with the next one.
/// </summary>
public sealed partial class JobRuns(IJobHeartbeatStore store, TimeProvider timeProvider, ILogger<JobRuns> logger)
{
    /// <summary>How often a step whose outcome hasn't changed is saved, at most.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, StepRuns> _steps = new();

    /// <summary>The step's worker has started here, so the step counts for the health check from now.</summary>
    public void Started(JobStep step) =>
        _steps.AddOrUpdate(step.Key, _ => new StepRuns(step) { StartedAt = timeProvider.GetUtcNow() }, (_, runs) => runs with { StartedAt = runs.StartedAt ?? timeProvider.GetUtcNow() });

    /// <summary>Records a run of the step that just finished, and saves it if it's time to.</summary>
    public async Task RecordAsync(JobStep step, bool succeeded, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Each step is recorded by its own worker, one run at a time, so nothing else replaces its entry meanwhile.
        var runs = _steps.GetOrAdd(step.Key, _ => new StepRuns(step));
        var save = runs.SavedAt is not { } savedAt || now - savedAt >= SaveEvery || runs.SavedSucceeded != succeeded;
        runs = succeeded ? runs with { LastSucceededAt = now } : runs with { LastFailedAt = now };
        if (save)
        {
            runs = runs with { SavedAt = now, SavedSucceeded = succeeded };
        }

        _steps[step.Key] = runs;
        if (!save)
        {
            return;
        }

        try
        {
            await store.SaveAsync(step.Key, runs.LastSucceededAt, runs.LastFailedAt, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
        {
            // The deploy's migrations haven't added the table yet (#225). It's in memory, and the next save includes it.
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A warning, not an error: the step itself worked, and the next save includes this run.
            LogSaveFailed(exception, step.Name);
        }
    }

    /// <summary>This replica's steps whose worker has started, and where each stands now. The health check goes by these.</summary>
    public IReadOnlyList<JobStepStatus> Running()
    {
        var now = timeProvider.GetUtcNow();
        return [.. _steps.Values
            .Where(runs => runs.StartedAt is not null)
            .Select(runs => new JobStepStatus(runs.Step, runs.Step.HealthAt(now, runs.StartedAt, runs.LastSucceededAt, runs.LastFailedAt), runs.LastSucceededAt, runs.LastFailedAt))];
    }

    /// <summary>
    /// Every step, for /manage: the latest times from any replica (this one's own included, in case they aren't saved yet),
    /// and where each stands. A step whose worker doesn't run here is Off.
    /// </summary>
    public async Task<JobStatusReport> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JobHeartbeat> saved;
        var notMigrated = false;
        try
        {
            saved = await store.LoadAsync(cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            saved = [];
            notMigrated = true;
        }

        var now = timeProvider.GetUtcNow();
        var steps = JobSteps.All.Select(step =>
        {
            var mine = _steps.GetValueOrDefault(step.Key);
            var theirs = saved.FirstOrDefault(h => h.Step == step.Key);
            var succeeded = Later(mine?.LastSucceededAt, theirs?.LastSucceededAt);
            var failed = Later(mine?.LastFailedAt, theirs?.LastFailedAt);
            return new JobStepStatus(step, step.HealthAt(now, mine?.StartedAt, succeeded, failed), succeeded, failed);
        });

        return new JobStatusReport([.. steps], notMigrated);
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the last run of {Step}. It's saved with the next one")]
    private partial void LogSaveFailed(Exception exception, string step);

    /// <param name="StartedAt">When the step's worker started here; null if it doesn't run here.</param>
    /// <param name="SavedAt">When this replica last saved the step.</param>
    /// <param name="SavedSucceeded">Whether the run it last saved succeeded.</param>
    private sealed record StepRuns(
        JobStep Step,
        DateTimeOffset? StartedAt = null,
        DateTimeOffset? LastSucceededAt = null,
        DateTimeOffset? LastFailedAt = null,
        DateTimeOffset? SavedAt = null,
        bool? SavedSucceeded = null);
}
