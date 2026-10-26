using CSAdvent.Web.Data;

namespace CSAdvent.Web.DeadLinks;

/// <summary>Where a post's check stands, as the rules see it.</summary>
public sealed record LinkCheckState(
    LinkStatus Status,
    int Failures,
    DateTimeOffset? FailingSince,
    DateTimeOffset? DeadSince,
    string? Detail,
    DateTimeOffset? CheckedAt,
    DateTimeOffset NextCheckAt)
{
    public static LinkCheckState Of(LinkCheck check) =>
        new(check.Status, check.Failures, check.FailingSince, check.DeadSince, check.Detail, check.CheckedAt, check.NextCheckAt);

    public void ApplyTo(LinkCheck check)
    {
        check.Status = Status;
        check.Failures = Failures;
        check.FailingSince = FailingSince;
        check.DeadSince = DeadSince;
        check.Detail = Detail;
        check.CheckedAt = CheckedAt;
        check.NextCheckAt = NextCheckAt;
    }
}

/// <summary>
/// What a check does to a post's state (#201). A post is only dead after <see cref="LinkCheckOptions.FailuresBeforeDead"/>
/// failed checks in a row, a day or more apart, so a site that's down for an afternoon isn't. A post that loads is working again,
/// whatever it was, and a site that turns the check away tells us nothing.
/// </summary>
public static class LinkCheckRules
{
    /// <param name="jitter">From 0 to 1: spreads the monthly checks over a few days, so they don't all come round together.</param>
    public static LinkCheckState After(LinkCheckState state, LinkProbe probe, DateTimeOffset now, LinkCheckOptions options, double jitter)
    {
        var recheck = now + options.RecheckAfter * (0.9 + (0.2 * Math.Clamp(jitter, 0, 1)));
        switch (probe.Outcome)
        {
            case LinkOutcome.Loaded:
                return new LinkCheckState(LinkStatus.Working, 0, null, null, probe.Detail, now, recheck);

            case LinkOutcome.Refused:
                // No telling either way: a dead post stays dead (its copy is still the best we have), anything else is unknown.
                return state.Status == LinkStatus.Dead
                    ? state with { Detail = probe.Detail, CheckedAt = now, NextCheckAt = recheck }
                    : new LinkCheckState(LinkStatus.Refused, 0, null, null, probe.Detail, now, recheck);

            default:
                var failures = state.Failures + 1;
                var failingSince = state.FailingSince ?? now;
                return failures >= options.FailuresBeforeDead
                    ? new LinkCheckState(LinkStatus.Dead, failures, failingSince, state.DeadSince ?? now, probe.Detail, now, recheck)
                    : new LinkCheckState(LinkStatus.Failing, failures, failingSince, null, probe.Detail, now, now + (options.RetryAfter * failures));
        }
    }

    /// <summary>
    /// When a dead post's archived copy is next looked up: after <see cref="LinkCheckOptions.ArchiveRecheckAfter"/> if the archive
    /// had no copy, or in a few hours if it didn't answer. Stored as the time it counts as last asked.
    /// </summary>
    public static DateTimeOffset ArchiveCheckedAt(ArchiveAnswer answer, DateTimeOffset now, LinkCheckOptions options) =>
        answer.Answered ? now : now - options.ArchiveRecheckAfter + UnansweredRetry;

    /// <summary>How soon the archive is asked again when it didn't answer.</summary>
    public static readonly TimeSpan UnansweredRetry = TimeSpan.FromHours(6);
}
