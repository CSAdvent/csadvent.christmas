using CSAdvent.Web.Data;

namespace CSAdvent.Web.Email.Queue;

/// <summary>An email the queue gave up on: it failed for good, or expired unsent.</summary>
public sealed record UndeliveredEmail(int Id, string Kind, string? Reference, QueuedEmailStatus Status);

/// <summary>How many emails the email service has taken lately, by the queue's own <c>sent_at</c> times.</summary>
/// <param name="ListLastMinute">Of the last minute's, how many were list mail.</param>
public readonly record struct RecentSends(int LastMinute, int LastHour, int ListLastMinute = 0);

/// <summary>
/// What <see cref="EmailQueueDrain"/> needs from the outbox (<see cref="EmailOutboxStore"/>), so its rules can be tested
/// without a database. Which emails are due, and in what order, is <see cref="EmailOutboxQueries"/>, shared by both.
/// </summary>
public interface IEmailOutboxStore
{
    /// <summary>Whether any email is ready to go (or to expire), before taking the lock.</summary>
    Task<bool> AnyDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the lock that lets one replica at a time drain the queue, or returns null if another replica has it. Disposing
    /// it lets go.
    /// </summary>
    Task<IAsyncDisposable?> TryLockAsync(CancellationToken cancellationToken);

    /// <summary>Marks pending emails whose <c>send_before</c> has passed as Expired, and returns them.</summary>
    Task<IReadOnlyList<UndeliveredEmail>> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<RecentSends> CountSentAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Takes up to <paramref name="count"/> due emails, list mail or not, in sending order, and holds them until
    /// <paramref name="lockedUntil"/> so nothing else sends them.
    /// </summary>
    Task<IReadOnlyList<QueuedEmail>> ClaimAsync(DateTimeOffset now, int count, bool listMail, DateTimeOffset lockedUntil, CancellationToken cancellationToken);

    Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt, and when to try again.</summary>
    Task MarkRetryAsync(int id, int attempts, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken);

    /// <summary>Gives up on an email, for an organizer to retry or discard.</summary>
    Task MarkFailedAsync(int id, int attempts, DateTimeOffset at, string error, CancellationToken cancellationToken);

    /// <summary>Lets go of emails a pass took but didn't try, so the next pass (anywhere) can.</summary>
    Task ReleaseAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);
}
