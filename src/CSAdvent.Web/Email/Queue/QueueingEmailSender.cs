using CSAdvent.Web.Data;

namespace CSAdvent.Web.Email.Queue;

/// <summary>
/// The app's <see cref="IEmailSender"/> (#152): puts each email in the outbox for <see cref="EmailOutboxWorker"/>, which sends
/// it within the email service's limits, and wakes that worker. Callers are unchanged, except that "sent" now means queued,
/// which only fails when the database does: the caller's catch block still undoes whatever it recorded.
/// </summary>
/// <remarks>
/// Deploys run the migrations before the new revision starts (#151), but a revision started without them would find the
/// table, or a column a later migration adds, missing. Until it's there, emails go straight through the transport as they
/// used to, so none is lost.
/// </remarks>
public sealed partial class QueueingEmailSender(
    [FromKeyedServices(EmailRegistration.TransportKey)] IEmailSender transport,
    EmailOptOutLinks links,
    EmailQueueSignal signal,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<QueueingEmailSender> logger) : IEmailSender
{
    /// <summary>The kind for an email that doesn't say which it is.</summary>
    public const string UnknownKind = "other";

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // The sender is shared by the whole app, so the outbox is written in a scope of its own.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();
        db.EmailOutbox.Add(new QueuedEmail
        {
            Kind = email.Kind ?? UnknownKind,
            Priority = PriorityOf(email),
            Reference = email.Reference,
            ToAddress = email.To.Address,
            ToName = email.To.Name,
            ReplyTo = email.ReplyTo,
            Subject = email.Subject,
            Html = email.Html,
            Text = email.Text,
            UnsubscribeUrl = email.UnsubscribeUrl,
            Transactional = email.Transactional,
            CreatedAt = now,

            // PostgreSQL only takes UTC, and the schedule's times (a reveal, say) are Eastern.
            SendBefore = email.SendBefore?.ToUniversalTime(),
            Status = QueuedEmailStatus.Pending,
            NextAttemptAt = now,
        });

        try
        {
            // Never cancelled: a caller that has just recorded the email (in sent_reminders, say) needs it queued, even while
            // the app is stopping. It's one insert.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            LogSentDirectly(email.Kind ?? UnknownKind);
            try
            {
                await transport.SendAsync(email, cancellationToken);
            }
            catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
            {
                // Callers log what's thrown, and a transport's message can quote the recipient's address (#168), so they get
                // the queue's description of it instead.
                throw new EmailNotSentException(EmailFailures.Describe(failure));
            }

            return;
        }

        signal.Notify();
    }

    /// <summary>
    /// How soon an email goes: anything to the organizers' inbox first, then what its sender asked for, else transactional
    /// emails first and the rest after them.
    /// </summary>
    public EmailPriority PriorityOf(OutgoingEmail email) =>
        links.IsOrganizers(email.To.Address) ? EmailPriority.Urgent
        : email.Priority ?? (email.Transactional ? EmailPriority.Urgent : EmailPriority.Normal);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent a {Kind} email straight away: the email queue isn't migrated to this version yet")]
    private partial void LogSentDirectly(string kind);
}

/// <summary>An email sent straight away that didn't go, described by <see cref="EmailFailures.Describe"/>: never with an address.</summary>
public sealed class EmailNotSentException(string description) : Exception(description);
