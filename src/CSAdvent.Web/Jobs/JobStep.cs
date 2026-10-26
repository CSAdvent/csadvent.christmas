using System.Globalization;

namespace CSAdvent.Web.Jobs;

/// <summary>Where a background job step stands, on /manage and for the health check (#172).</summary>
public enum JobHealth
{
    /// <summary>Its worker doesn't run here: the Testing environment turns most of them off.</summary>
    Off,

    /// <summary>Its worker has started, and its first run hasn't finished yet.</summary>
    Waiting,

    /// <summary>Its last run succeeded.</summary>
    Succeeding,

    /// <summary>Its last run failed, but it succeeded recently enough not to be late yet.</summary>
    Failing,

    /// <summary>It hasn't succeeded for longer than <see cref="JobStep.LateAfter"/>, which turns <c>/health</c> Degraded.</summary>
    Late,
}

/// <summary>
/// One step of a background job, such as the reminders or the backup asks (#172): what /manage and the logs call it, and how
/// often it runs. Each step's runs are recorded on their own (<see cref="JobRuns"/>), so one that keeps failing shows up even
/// while the rest of its worker carries on (#147).
/// </summary>
/// <param name="Key">What its runs are recorded by, in <c>job_heartbeats</c>. Never change one.</param>
/// <param name="Name">What organizers see on /manage, and what the logs call it.</param>
/// <param name="Interval">How often it runs.</param>
public sealed record JobStep(string Key, string Name, TimeSpan Interval)
{
    /// <summary>The least time any step can go without succeeding before it's late.</summary>
    public static readonly TimeSpan ShortestLateAfter = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long it can go without succeeding before it's late: four runs, and at least 30 minutes, so a slow run, a short
    /// database outage or a deploy doesn't make it late.
    /// </summary>
    public TimeSpan LateAfter => Interval * 4 > ShortestLateAfter ? Interval * 4 : ShortestLateAfter;

    /// <summary>"Every 15 minutes", or "Every hour", for /manage.</summary>
    public string Every => "Every " + (Duration(Interval) switch
    {
        ['1', ' ', .. var unit] => unit,
        var duration => duration,
    });

    /// <summary>"15 minutes", "1 hour" or "2 seconds", in whole units.</summary>
    public static string Duration(TimeSpan span) => span switch
    {
        { TotalHours: >= 1 } when span.TotalHours % 1 == 0 => Plural((int)span.TotalHours, "hour"),
        { TotalMinutes: >= 1 } when span.TotalMinutes % 1 == 0 => Plural((int)span.TotalMinutes, "minute"),
        _ => Plural((int)Math.Round(span.TotalSeconds), "second"),
    };

    /// <summary>
    /// Where the step stands at <paramref name="now"/>. A step whose worker doesn't run here is <see cref="JobHealth.Off"/>;
    /// otherwise it's late once it has gone <see cref="LateAfter"/> without succeeding, counting from when its worker started
    /// if that's later, so a replica that has just started isn't late because of an old record.
    /// </summary>
    /// <param name="startedAt">When its worker started on this replica, or null if it doesn't run here.</param>
    public JobHealth HealthAt(DateTimeOffset now, DateTimeOffset? startedAt, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt)
    {
        if (startedAt is not { } started)
        {
            return JobHealth.Off;
        }

        var since = lastSucceededAt is { } succeeded && succeeded > started ? succeeded : started;
        return now - since > LateAfter ? JobHealth.Late
            : lastFailedAt is { } failed && (lastSucceededAt is not { } last || failed > last) ? JobHealth.Failing
            : lastSucceededAt is null ? JobHealth.Waiting
            : JobHealth.Succeeding;
    }

    private static string Plural(int count, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? "" : "s")}");
}

/// <summary>A step of a <see cref="PeriodicWorker"/>, and what it does with its own scope's services at the given time.</summary>
public sealed record PeriodicStep(JobStep Step, Func<IServiceProvider, DateTimeOffset, CancellationToken, Task> RunAsync);
