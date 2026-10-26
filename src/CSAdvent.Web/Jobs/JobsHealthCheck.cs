using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CSAdvent.Web.Jobs;

/// <summary>
/// <c>/health</c>'s background job check (#172): Degraded when one of this replica's steps hasn't succeeded for longer than
/// its <see cref="JobStep.LateAfter"/>, so the uptime check can notice a job that has stopped. Never Unhealthy: Container Apps'
/// readiness probe asks <c>/health</c>, and Unhealthy would take every replica out of service over a late email, while
/// Degraded still answers 200. Steps whose worker doesn't run here (most of them, in the Testing environment) don't count.
/// </summary>
public sealed class JobsHealthCheck(JobRuns runs) : IHealthCheck
{
    public const string Name = "background-jobs";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var late = runs.Running().Where(s => s.Health is JobHealth.Late).Select(s => s.Step.Name).ToList();
        return Task.FromResult(late.Count == 0
            ? HealthCheckResult.Healthy("Every background job step has succeeded recently.")
            : HealthCheckResult.Degraded($"Not succeeded for too long: {string.Join(", ", late)}."));
    }
}
