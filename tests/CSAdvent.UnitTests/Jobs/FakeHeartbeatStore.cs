using CSAdvent.Web.Data;
using CSAdvent.Web.Jobs;

namespace CSAdvent.UnitTests.Jobs;

/// <summary><c>job_heartbeats</c> in memory, recording every save, or failing every call with <see cref="Failure"/>.</summary>
internal sealed class FakeHeartbeatStore : IJobHeartbeatStore
{
    private readonly Dictionary<string, JobHeartbeat> _rows = [];

    /// <summary>Every save, in order.</summary>
    public List<JobHeartbeat> Saved { get; } = [];

    /// <summary>
    /// Released after each save, which comes after the run is recorded in memory: what a test waits on when a worker runs on
    /// a task of its own.
    /// </summary>
    public SemaphoreSlim Saves { get; } = new(0);

    public Exception? Failure { get; set; }

    /// <summary>What another replica saved.</summary>
    public void Add(JobHeartbeat row) => _rows[row.Step] = row;

    public Task SaveAsync(string step, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        var row = new JobHeartbeat { Step = step, LastSucceededAt = lastSucceededAt, LastFailedAt = lastFailedAt };
        Saved.Add(row);
        _rows[step] = _rows.TryGetValue(step, out var saved)
            ? new JobHeartbeat { Step = step, LastSucceededAt = Later(saved.LastSucceededAt, lastSucceededAt), LastFailedAt = Later(saved.LastFailedAt, lastFailedAt) }
            : row;
        Saves.Release();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobHeartbeat>> LoadAsync(CancellationToken cancellationToken) =>
        Failure is not null ? Task.FromException<IReadOnlyList<JobHeartbeat>>(Failure) : Task.FromResult<IReadOnlyList<JobHeartbeat>>([.. _rows.Values]);

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;
}
