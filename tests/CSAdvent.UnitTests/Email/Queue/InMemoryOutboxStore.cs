using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;

namespace CSAdvent.UnitTests.Email.Queue;

/// <summary>
/// The outbox in memory, for the drain's rules. Which emails are due and their order come from <see cref="EmailOutboxQueries"/>,
/// the same as the database's; the advisory lock is a semaphore.
/// </summary>
internal sealed class InMemoryOutboxStore : IEmailOutboxStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _nextId = 1;

    public List<QueuedEmail> Emails { get; } = [];

    public QueuedEmail Add(
        DateTimeOffset now,
        EmailPriority priority = EmailPriority.Urgent,
        string kind = "claim-received",
        DateTimeOffset? sendBefore = null,
        string? reference = null,
        string? to = null)
    {
        var id = _nextId++;
        var email = new QueuedEmail
        {
            Id = id,
            Kind = kind,
            Priority = priority,
            Reference = reference,
            ToAddress = to ?? $"person{id}@example.com",
            ReplyTo = "hello@csadvent.christmas",
            Subject = $"Email {id}",
            Html = "<p>Hi</p>",
            Text = "Hi",
            CreatedAt = now,
            SendBefore = sendBefore,
            Status = QueuedEmailStatus.Pending,
            NextAttemptAt = now,
        };
        Emails.Add(email);
        return email;
    }

    public QueuedEmail this[int id] => Emails.Single(e => e.Id == id);

    public Task<bool> AnyDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(Emails.AsQueryable().Due(now).Any() || Emails.AsQueryable().PastSendBefore(now).Any());

    public async Task<IAsyncDisposable?> TryLockAsync(CancellationToken cancellationToken) =>
        await _lock.WaitAsync(0, cancellationToken) ? new Held(_lock) : null;

    public Task<IReadOnlyList<UndeliveredEmail>> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = Emails.AsQueryable().PastSendBefore(now).ToList();
        foreach (var email in expired)
        {
            email.Status = QueuedEmailStatus.Expired;
            email.FinishedAt = now;
            email.LockedUntil = null;
        }

        return Task.FromResult<IReadOnlyList<UndeliveredEmail>>([.. expired.Select(e => new UndeliveredEmail(e.Id, e.Kind, e.Reference, QueuedEmailStatus.Expired))]);
    }

    public Task<RecentSends> CountSentAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(new RecentSends(
        Emails.Count(e => e.SentAt > now - TimeSpan.FromMinutes(1)),
        Emails.Count(e => e.SentAt > now - TimeSpan.FromHours(1)),
        Emails.Count(e => e.SentAt > now - TimeSpan.FromMinutes(1) && e.Priority.IsListMail())));

    public Task<IReadOnlyList<QueuedEmail>> ClaimAsync(DateTimeOffset now, int count, bool listMail, DateTimeOffset lockedUntil, CancellationToken cancellationToken)
    {
        var claimed = count <= 0 ? [] : Emails.AsQueryable().Due(now).ListMail(listMail).InSendingOrder().Take(count).ToList();
        foreach (var email in claimed)
        {
            email.LockedUntil = lockedUntil;
        }

        return Task.FromResult<IReadOnlyList<QueuedEmail>>(claimed);
    }

    public Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var email = this[id];
        email.Status = QueuedEmailStatus.Sent;
        email.SentAt = at;
        email.FinishedAt = at;
        email.LockedUntil = null;
        return Task.CompletedTask;
    }

    public Task MarkRetryAsync(int id, int attempts, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken)
    {
        var email = this[id];
        email.Attempts = attempts;
        email.NextAttemptAt = nextAttemptAt;
        email.LastError = error;
        email.LockedUntil = null;
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(int id, int attempts, DateTimeOffset at, string error, CancellationToken cancellationToken)
    {
        var email = this[id];
        email.Status = QueuedEmailStatus.Failed;
        email.Attempts = attempts;
        email.FinishedAt = at;
        email.LastError = error;
        email.LockedUntil = null;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        foreach (var email in Emails.Where(e => ids.Contains(e.Id) && e.Status == QueuedEmailStatus.Pending))
        {
            email.LockedUntil = null;
        }

        return Task.CompletedTask;
    }

    private sealed class Held(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The transport: records what it sent, and fails or waits when a test says so.</summary>
internal sealed class FakeTransport : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    /// <summary>Every attempt, sent or not, by subject.</summary>
    public List<string> Attempts { get; } = [];

    /// <summary>The exception to throw for an email, if any.</summary>
    public Func<OutgoingEmail, Exception?> Fail { get; set; } = _ => null;

    /// <summary>Runs before each send, to hold it up or move the clock.</summary>
    public Func<OutgoingEmail, Task> Before { get; set; } = _ => Task.CompletedTask;

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        Attempts.Add(email.Subject);
        await Before(email);
        if (Fail(email) is { } exception)
        {
            throw exception;
        }

        Sent.Add(email);
    }
}

/// <summary>What the drain told the senders about.</summary>
internal sealed class RecordingUndeliveredHandler : IUndeliveredEmailHandler
{
    public List<UndeliveredEmail> Told { get; } = [];

    public Task HandleAsync(UndeliveredEmail email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Told.Add(email);
        return Task.CompletedTask;
    }
}
