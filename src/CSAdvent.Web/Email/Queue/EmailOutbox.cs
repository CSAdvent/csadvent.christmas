using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email.Queue;

/// <summary>Emails waiting of one kind and priority.</summary>
public sealed record WaitingEmails(string Kind, EmailPriority Priority, int Count, DateTimeOffset OldestQueuedAt);

/// <summary>An email the queue gave up on, for an organizer to retry or discard.</summary>
public sealed record FailedEmail(int Id, string Kind, string ToAddress, string Subject, int Attempts, string? LastError, DateTimeOffset? FailedAt);

/// <summary>The queue at a glance, for <c>/manage/emails/queue</c>.</summary>
/// <param name="ExpiredToday">Emails that expired unsent in the last 24 hours.</param>
/// <param name="TableMissing">The migrations haven't created the queue yet, so emails are going straight out.</param>
public sealed record EmailQueueStatus(IReadOnlyList<WaitingEmails> Waiting, RecentSends Sent, int ExpiredToday, IReadOnlyList<FailedEmail> Failed, bool TableMissing = false)
{
    public static readonly EmailQueueStatus Missing = new([], new RecentSends(0, 0), 0, [], TableMissing: true);
}

/// <summary>How far one sender's emails have got (an announcement's, a season's invitations).</summary>
/// <param name="NotSent">Failed or expired, as long as the queue keeps them (a week).</param>
public sealed record QueueProgress(int Waiting, int Sent, int NotSent)
{
    public static readonly QueueProgress None = new(0, 0, 0);
}

/// <summary>
/// The email queue (#152) for organizers and senders: what's waiting, sent and failed; retrying or discarding a failed email;
/// taking back emails nobody wants any more (an unsubscribe); and how much list mail the queue has room for. Every read copes
/// with the queue's table or a column not being there yet (the deploy runs the migrations after the new revision starts).
/// </summary>
public sealed class EmailOutbox(AdventDbContext db, EmailQueueSignal signal, IOptions<EmailQueueOptions> options, TimeProvider timeProvider)
{
    public EmailQueueOptions Limits => options.Value;

    public async Task<EmailQueueStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var dayAgo = now - TimeSpan.FromDays(1);
        try
        {
            var waiting = await db.EmailOutbox
                .Where(e => e.Status == QueuedEmailStatus.Pending)
                .GroupBy(e => new { e.Kind, e.Priority })
                .Select(g => new WaitingEmails(g.Key.Kind, g.Key.Priority, g.Count(), g.Min(e => e.CreatedAt)))
                .ToListAsync(cancellationToken);
            var failed = await db.EmailOutbox
                .Where(e => e.Status == QueuedEmailStatus.Failed)
                .OrderByDescending(e => e.FinishedAt).ThenByDescending(e => e.Id)
                .Select(e => new FailedEmail(e.Id, e.Kind, e.ToAddress, e.Subject, e.Attempts, e.LastError, e.FinishedAt))
                .ToListAsync(cancellationToken);
            return new EmailQueueStatus(
                [.. waiting.OrderBy(w => w.Priority).ThenBy(w => w.OldestQueuedAt)],
                await EmailOutboxStore.CountSentAsync(db, now, cancellationToken),
                await db.EmailOutbox.CountAsync(e => e.Status == QueuedEmailStatus.Expired && e.FinishedAt > dayAgo, cancellationToken),
                failed);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            return EmailQueueStatus.Missing;
        }
    }

    /// <summary>How many emails the queue gave up on, for the badge in the organizer area's navigation.</summary>
    public async Task<int> CountFailedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.EmailOutbox.CountAsync(e => e.Status == QueuedEmailStatus.Failed, cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            return 0;
        }
    }

    /// <summary>Sends a failed email again from scratch, whatever its deadline was. False if it isn't failed any more.</summary>
    public async Task<bool> RetryAsync(int id, CancellationToken cancellationToken = default) =>
        await RetryWhereAsync(db.EmailOutbox.Where(e => e.Id == id), cancellationToken) == 1;

    /// <summary>Sends every failed email again, after the email service has come back, say.</summary>
    public Task<int> RetryAllFailedAsync(CancellationToken cancellationToken = default) => RetryWhereAsync(db.EmailOutbox, cancellationToken);

    /// <summary>Deletes a failed email. False if it isn't failed any more.</summary>
    public async Task<bool> DiscardAsync(int id, CancellationToken cancellationToken = default) =>
        await db.EmailOutbox.Where(e => e.Id == id && e.Status == QueuedEmailStatus.Failed).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <summary>
    /// Takes back the emails still waiting to <paramref name="address"/> whose reference starts with
    /// <paramref name="referencePrefix"/>, such as an announcement to someone who's just unsubscribed.
    /// </summary>
    public async Task<int> CancelAsync(string address, string referencePrefix, CancellationToken cancellationToken = default)
    {
        var normalized = EmailOptOutLinks.Normalize(address);
        try
        {
            return await db.EmailOutbox
                .Where(e => e.Status == QueuedEmailStatus.Pending
                    && e.ToAddress.ToLower() == normalized
                    && e.Reference != null && e.Reference.StartsWith(referencePrefix))
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            return 0;
        }
    }

    /// <summary>
    /// How much more list mail the queue could send in the coming hour: what's left under
    /// <see cref="EmailQueueOptions.ListPerHour"/>, less the list mail already waiting. Senders that work out who gets an email
    /// when they queue it (past authors' invitations) queue no more than this, so it's decided shortly before it goes.
    /// </summary>
    public async Task<int> GetListRoomAsync(CancellationToken cancellationToken = default)
    {
        var hourAgo = timeProvider.GetUtcNow() - TimeSpan.FromHours(1);
        try
        {
            var sent = await db.EmailOutbox.CountAsync(e => e.SentAt > hourAgo, cancellationToken);
            var waiting = await db.EmailOutbox.CountAsync(e => e.Status == QueuedEmailStatus.Pending && e.Priority >= EmailPriorities.FirstListMail, cancellationToken);
            return Math.Max(0, options.Value.ListPerHour - sent - waiting);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            // Nothing's counted until the table's there, so queue nothing optional.
            return 0;
        }
    }

    /// <summary>How far the emails with each reference starting with <paramref name="referencePrefix"/> have got, by reference.</summary>
    public async Task<IReadOnlyDictionary<string, QueueProgress>> GetProgressAsync(string referencePrefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var counts = await db.EmailOutbox
                .Where(e => e.Reference != null && e.Reference.StartsWith(referencePrefix))
                .GroupBy(e => new { e.Reference, e.Status })
                .Select(g => new { g.Key.Reference, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken);
            return counts
                .GroupBy(c => c.Reference!)
                .ToDictionary(
                    g => g.Key,
                    g => new QueueProgress(
                        g.Where(c => c.Status is QueuedEmailStatus.Pending).Sum(c => c.Count),
                        g.Where(c => c.Status is QueuedEmailStatus.Sent).Sum(c => c.Count),
                        g.Where(c => c.Status is QueuedEmailStatus.Failed or QueuedEmailStatus.Expired).Sum(c => c.Count)));
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            return new Dictionary<string, QueueProgress>();
        }
    }

    public async Task<QueueProgress> GetProgressOfAsync(string reference, CancellationToken cancellationToken = default) =>
        (await GetProgressAsync(reference, cancellationToken)).GetValueOrDefault(reference, QueueProgress.None);

    /// <summary>
    /// Whether the outbox exists yet, asked without an error (<c>to_regclass</c>), so a worker waiting for the deploy's
    /// migrations doesn't fill the logs (and the error alert) with failed queries.
    /// </summary>
    public static async Task<bool> TableExistsAsync(AdventDbContext db, CancellationToken cancellationToken = default) =>
        await db.Database.SqlQuery<bool>($"SELECT to_regclass('email_outbox') IS NOT NULL AS \"Value\"").SingleAsync(cancellationToken);

    private async Task<int> RetryWhereAsync(IQueryable<QueuedEmail> emails, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var retried = await emails
            .Where(e => e.Status == QueuedEmailStatus.Failed)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Status, QueuedEmailStatus.Pending)
                    .SetProperty(e => e.Attempts, 0)
                    .SetProperty(e => e.NextAttemptAt, now)
                    .SetProperty(e => e.SendBefore, (DateTimeOffset?)null)
                    .SetProperty(e => e.FinishedAt, (DateTimeOffset?)null)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null),
                cancellationToken);
        if (retried > 0)
        {
            signal.Notify();
        }

        return retried;
    }
}
