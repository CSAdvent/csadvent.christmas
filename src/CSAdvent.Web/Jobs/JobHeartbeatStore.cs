using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Jobs;

/// <summary>Where <see cref="JobRuns"/> keeps every replica's step runs: <c>job_heartbeats</c>, or a fake in the unit tests.</summary>
public interface IJobHeartbeatStore
{
    /// <summary>Saves the step's times, keeping whichever of each is later: what's saved, or these.</summary>
    Task SaveAsync(string step, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobHeartbeat>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <c>job_heartbeats</c>. <see cref="JobRuns"/> is a singleton, like the workers that use it, so each call gets a scope of
/// its own for the database.
/// </summary>
public sealed class JobHeartbeatStore(IServiceScopeFactory scopeFactory) : IJobHeartbeatStore
{
    public async Task SaveAsync(string step, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();

        // Replicas save the same step, so neither time ever goes back (PostgreSQL's GREATEST ignores nulls).
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO job_heartbeats (step, last_succeeded_at, last_failed_at) VALUES ({step}, {lastSucceededAt}, {lastFailedAt})
            ON CONFLICT (step) DO UPDATE SET
                last_succeeded_at = GREATEST(job_heartbeats.last_succeeded_at, excluded.last_succeeded_at),
                last_failed_at = GREATEST(job_heartbeats.last_failed_at, excluded.last_failed_at)
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<JobHeartbeat>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AdventDbContext>().JobHeartbeats.AsNoTracking().ToListAsync(cancellationToken);
    }
}
