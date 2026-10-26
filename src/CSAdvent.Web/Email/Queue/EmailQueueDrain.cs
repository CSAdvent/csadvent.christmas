using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email.Queue;

/// <summary>How many emails a pass could send.</summary>
/// <param name="Total">In all: whatever's left of the minute's and the hour's limits.</param>
/// <param name="ListUnderCeiling">How far the hour's sends are under <see cref="EmailQueueOptions.ListPerHour"/>.</param>
/// <param name="ListThisMinute">What's left of the minute's share of list mail (<see cref="EmailQueueOptions.ListPerMinute"/>).</param>
public readonly record struct EmailBudget(int Total, int ListUnderCeiling, int ListThisMinute)
{
    /// <summary>The budget from what every replica has sent, by the queue's own <c>sent_at</c> times.</summary>
    public static EmailBudget From(EmailQueueOptions options, RecentSends sent) => new(
        Math.Max(0, Math.Min(options.PerMinute - sent.LastMinute, options.PerHour - sent.LastHour)),
        options.ListPerHour - sent.LastHour,
        options.ListPerMinute - sent.ListLastMinute);

    /// <summary>How much list mail can go once <paramref name="others"/> other emails have been taken this pass, which count towards the ceiling too.</summary>
    public int ListRoom(int others) => Math.Max(0, Math.Min(Math.Min(ListUnderCeiling - others, ListThisMinute), Total - others));

    /// <summary>The budget, but no more than <paramref name="most"/> in all.</summary>
    public EmailBudget AtMost(int most) => this with { Total = Math.Min(Total, most) };
}

/// <summary>What a pass did.</summary>
public sealed record EmailQueuePass(int Sent, int Retrying, int Failed, int Expired)
{
    public static readonly EmailQueuePass Nothing = new(0, 0, 0, 0);

    /// <summary>Another replica is draining the queue.</summary>
    public bool Busy { get; init; }

    /// <summary>The email service asked us to slow down: nothing more goes before this.</summary>
    public DateTimeOffset? PausedUntil { get; init; }

    /// <summary>
    /// It sent as many as a pass may (<see cref="EmailQueueOptions.MaxPerPass"/>), so there may be more due: the worker starts
    /// the next pass straight away.
    /// </summary>
    public bool More { get; init; }
}

/// <summary>Told when the queue gives up on an email (it failed for good, or expired unsent), for senders that need to know.</summary>
public interface IUndeliveredEmailHandler
{
    Task HandleAsync(UndeliveredEmail email, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// One pass over the email queue (#152), run by <see cref="EmailOutboxWorker"/>:
/// <list type="number">
/// <item>Takes the advisory lock, so only one replica drains at a time.</item>
/// <item>Marks emails past their <c>send_before</c> as Expired.</item>
/// <item>Works out what the limits allow (<see cref="EmailBudget"/>), from every replica's sends in the last minute and hour.</item>
/// <item>Takes that many due emails in priority order, oldest first within a priority, list mail only with what's left of
/// its share (under <see cref="EmailQueueOptions.ListPerHour"/>, and spread across the hour), and sends them one at a time
/// through the transport (opt-outs are checked there, at send time).</item>
/// </list>
/// A failed send is tried again later (1, 5 and 15 minutes, then hourly) and ends the pass, since if the email service is down
/// the rest would only fail too; after <see cref="EmailQueueOptions.MaxAttempts"/> it's Failed, for an organizer to retry. A
/// throttling answer (429) ends the pass without counting against the email, and pauses the queue. Senders that need to know
/// when an email is given up on (a backup ask, say) hear through <see cref="IUndeliveredEmailHandler"/>.
/// </summary>
public sealed partial class EmailQueueDrain(
    IEmailOutboxStore store,
    [FromKeyedServices(EmailRegistration.TransportKey)] IEmailSender transport,
    IServiceProvider services,
    IOptions<EmailQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailQueueDrain> logger)
{
    public async Task<EmailQueuePass> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (!await store.AnyDueAsync(now, cancellationToken))
        {
            return EmailQueuePass.Nothing;
        }

        await using var held = await store.TryLockAsync(cancellationToken);
        if (held is null)
        {
            return EmailQueuePass.Nothing with { Busy = true };
        }

        var expired = await store.ExpireAsync(now, cancellationToken);
        foreach (var email in expired)
        {
            LogExpired(email.Kind, email.Id);
            await TellSenderAsync(email, now);
        }

        var pass = EmailQueuePass.Nothing with { Expired = expired.Count };

        // No more than a pass's worth, so whatever's queued meanwhile, transactional emails first, goes in the next pass, a
        // moment later, rather than after a long batch of list mail (#198).
        var budget = EmailBudget.From(options.Value, await store.CountSentAsync(now, cancellationToken)).AtMost(options.Value.MaxPerPass);
        if (budget.Total == 0)
        {
            return pass;
        }

        // Everything but list mail first; then list mail, with what's left of its share once those count too.
        var lockedUntil = now + options.Value.Lease;
        var batch = new List<QueuedEmail>(await store.ClaimAsync(now, budget.Total, listMail: false, lockedUntil, cancellationToken));
        if (budget.ListRoom(batch.Count) is > 0 and var listRoom)
        {
            batch.AddRange(await store.ClaimAsync(now, listRoom, listMail: true, lockedUntil, cancellationToken));
        }

        // Halfway through the lease, stop starting sends, so nothing this pass holds is picked up again while it's sending.
        var stopStartingAt = now + options.Value.Lease / 2;
        for (var i = 0; i < batch.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested || timeProvider.GetUtcNow() >= stopStartingAt)
            {
                await store.ReleaseAsync(IdsFrom(batch, i), CancellationToken.None);
                break;
            }

            var email = batch[i];
            try
            {
                // Not cancelled when the app stops: a send that's started finishes (or times out) and is recorded, so it isn't
                // sent again by the next replica.
                using var timeout = new CancellationTokenSource(options.Value.SendTimeout, timeProvider);
                await transport.SendAsync(ToOutgoing(email), timeout.Token);
            }
            catch (Exception exception)
            {
                var failure = EmailFailures.Classify(exception);
                if (failure.Kind is EmailFailureKind.Throttled)
                {
                    var pause = failure.RetryAfter ?? options.Value.ThrottlePause;
                    LogThrottled(failure.Description, pause);
                    await store.ReleaseAsync(IdsFrom(batch, i), CancellationToken.None);
                    return pass with { PausedUntil = timeProvider.GetUtcNow() + pause };
                }

                pass = await FailedAsync(email, failure, pass);
                await store.ReleaseAsync(IdsFrom(batch, i + 1), CancellationToken.None);
                return pass;
            }

            await store.MarkSentAsync(email.Id, timeProvider.GetUtcNow(), CancellationToken.None);
            pass = pass with { Sent = pass.Sent + 1 };
        }

        return pass with { More = pass.Sent >= options.Value.MaxPerPass };
    }

    /// <summary>The email as the transport takes it.</summary>
    public static OutgoingEmail ToOutgoing(QueuedEmail email) =>
        new(new EmailRecipient(email.ToAddress, email.ToName), email.ReplyTo, email.Subject, email.Html, email.Text, email.UnsubscribeUrl, email.Transactional)
        {
            Kind = email.Kind,
            Priority = email.Priority,
            SendBefore = email.SendBefore,
            Reference = email.Reference,
        };

    private async Task<EmailQueuePass> FailedAsync(QueuedEmail email, EmailFailure failure, EmailQueuePass pass)
    {
        var now = timeProvider.GetUtcNow();
        var attempts = email.Attempts + 1;
        if (attempts >= options.Value.MaxAttempts)
        {
            LogGaveUp(email.Kind, email.Id, attempts, failure.Description);
            await store.MarkFailedAsync(email.Id, attempts, now, failure.Description, CancellationToken.None);
            await TellSenderAsync(new UndeliveredEmail(email.Id, email.Kind, email.Reference, QueuedEmailStatus.Failed), now);
            return pass with { Failed = pass.Failed + 1 };
        }

        var next = now + EmailQueueOptions.BackoffAfter(attempts);
        LogRetrying(email.Kind, email.Id, attempts, options.Value.MaxAttempts, failure.Description, next);
        await store.MarkRetryAsync(email.Id, attempts, next, failure.Description, CancellationToken.None);
        return pass with { Retrying = pass.Retrying + 1 };
    }

    private async Task TellSenderAsync(UndeliveredEmail email, DateTimeOffset now)
    {
        foreach (var handler in services.GetServices<IUndeliveredEmailHandler>())
        {
            try
            {
                await handler.HandleAsync(email, now, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogHandlerFailed(exception, email.Kind, email.Id);
            }
        }
    }

    private static int[] IdsFrom(List<QueuedEmail> batch, int start) => [.. batch.Skip(start).Select(e => e.Id)];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Didn't send {Kind} email {EmailId} in time: it expired unsent")]
    private partial void LogExpired(string kind, int emailId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The email service is throttling ({Error}). Pausing the email queue for {Pause}")]
    private partial void LogThrottled(string error, TimeSpan pause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send {Kind} email {EmailId} (attempt {Attempt} of {MaxAttempts}): {Error}. Trying again at {NextAttemptAt}")]
    private partial void LogRetrying(string kind, int emailId, int attempt, int maxAttempts, string error, DateTimeOffset nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up on {Kind} email {EmailId} after {Attempts} attempts: {Error}. An organizer can retry it from the email queue")]
    private partial void LogGaveUp(string kind, int emailId, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't tell the sender that {Kind} email {EmailId} wasn't delivered")]
    private partial void LogHandlerFailed(Exception exception, string kind, int emailId);
}
