using CSAdvent.Web.Data;

namespace CSAdvent.Web.Snapshots;

/// <summary>Where a snapshot stands: what <see cref="SnapshotRules.After"/> works out and the service saves.</summary>
public sealed record SnapshotState(
    SnapshotStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? JobId,
    DateTimeOffset? RequestedAt,
    string? SnapshotUrl,
    DateTimeOffset? SavedAt,
    string? Detail)
{
    public static SnapshotState Of(PostSnapshot snapshot) => new(
        snapshot.Status,
        snapshot.Attempts,
        snapshot.NextAttemptAt,
        snapshot.JobId,
        snapshot.RequestedAt,
        snapshot.SnapshotUrl,
        snapshot.SavedAt,
        snapshot.Detail);
}

/// <summary>When a post is due its snapshots, and what the Wayback Machine's answers do to one (#261).</summary>
public static class SnapshotRules
{
    /// <summary>
    /// A post is due its live snapshot from its reveal, once the blurb check has seen it (so it's surely up), or else once its
    /// day is over, and until <see cref="SnapshotOptions.LiveWithin"/> after that. Older posts only get one after their season.
    /// </summary>
    /// <param name="seen">The blurb check passed it, or an organizer let it through.</param>
    public static bool IsLiveDue(DateTimeOffset revealsAt, DateTimeOffset dayEndsAt, bool seen, DateTimeOffset now, TimeSpan liveWithin) =>
        now >= revealsAt && (seen || now >= dayEndsAt) && now < dayEndsAt + liveWithin;

    /// <summary>
    /// A post is due its snapshot after the season once the season is over (the end of December 25), unless the dead-link
    /// check has found it gone or failing: the Wayback Machine only saves pages that load. One that loads again is due again.
    /// </summary>
    public static bool IsAfterSeasonDue(DateTimeOffset seasonEndsAt, LinkStatus? link, DateTimeOffset now) =>
        now >= seasonEndsAt && link is not (LinkStatus.Dead or LinkStatus.Failing);

    /// <summary>
    /// What an answer does to a snapshot that was <see cref="SnapshotStatus.Due"/> (asked for) or
    /// <see cref="SnapshotStatus.Capturing"/> (asked about).
    /// <list type="bullet">
    /// <item>Pending: it's capturing, since it was asked for. One still capturing after <see cref="SnapshotOptions.JobExpiry"/>
    /// is taken as not saved.</item>
    /// <item>Saved: done.</item>
    /// <item>Failed: a try. It's asked for again after <see cref="SnapshotOptions.RetryAfter"/>, doubling each time, until
    /// <see cref="SnapshotOptions.MaxAttempts"/>, when it's given up on.</item>
    /// <item>No answer: not the post's fault. A request is made again after <see cref="SnapshotOptions.RetryAfter"/>, and a
    /// capture is asked about again on the next run, until it's expired.</item>
    /// </list>
    /// </summary>
    public static SnapshotState After(SnapshotState before, CaptureAnswer answer, DateTimeOffset now, SnapshotOptions options)
    {
        var capturing = before.Status == SnapshotStatus.Capturing;
        var expired = capturing && before.RequestedAt is { } requested && now - requested >= options.JobExpiry;
        return answer.State switch
        {
            CaptureState.Saved => before with
            {
                Status = SnapshotStatus.Saved,
                SnapshotUrl = answer.SnapshotUrl,
                SavedAt = answer.SavedAt,
                JobId = null,
                Detail = null,
            },
            CaptureState.Failed => Failed(before, answer.Detail ?? "The Wayback Machine couldn't save it.", now, options),
            _ when expired => Failed(before, "The Wayback Machine didn't say it had saved it within an hour.", now, options),
            CaptureState.Pending => before with
            {
                Status = SnapshotStatus.Capturing,
                JobId = capturing ? before.JobId : answer.JobId,
                RequestedAt = capturing ? before.RequestedAt : now,
                Detail = null,
            },
            _ when capturing => before with { Detail = answer.Detail },
            _ => before with { NextAttemptAt = now + options.RetryAfter, Detail = answer.Detail },
        };
    }

    private static SnapshotState Failed(SnapshotState before, string detail, DateTimeOffset now, SnapshotOptions options)
    {
        var attempts = before.Attempts + 1;
        return before with
        {
            Status = attempts >= options.MaxAttempts ? SnapshotStatus.Failed : SnapshotStatus.Due,
            Attempts = attempts,
            NextAttemptAt = now + options.RetryAfter * Math.Pow(2, attempts - 1),
            JobId = null,
            Detail = detail,
        };
    }
}
