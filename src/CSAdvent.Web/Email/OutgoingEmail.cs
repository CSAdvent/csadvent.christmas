namespace CSAdvent.Web.Email;

public sealed record EmailRecipient(string Address, string? Name = null);

/// <summary>
/// A rendered email, ready for a transport: branded HTML with a plain-text alternative. Emails people can opt out of carry
/// an <see cref="UnsubscribeUrl"/>, sent as the one-click <c>List-Unsubscribe</c> header (RFC 8058) as well as in the footer.
/// </summary>
/// <param name="Transactional">About something the recipient signed up for, so it's sent even if they unsubscribed (<see cref="EmailContent.Transactional"/>).</param>
public sealed record OutgoingEmail(EmailRecipient To, string ReplyTo, string Subject, string Html, string Text, string? UnsubscribeUrl = null, bool Transactional = false)
{
    /// <summary>Which email it is, for the organizers' view of the queue: its <see cref="EmailCatalog"/> key (<see cref="EmailContent.Kind"/>).</summary>
    public string? Kind { get; init; }

    /// <summary>How soon it goes when the queue is busy. Left empty, <see cref="Queue.QueueingEmailSender.PriorityOf"/> decides.</summary>
    public EmailPriority? Priority { get; init; }

    /// <summary>When it's no use any more: the queue drops it (as Expired) rather than send it late.</summary>
    public DateTimeOffset? SendBefore { get; init; }

    /// <summary>What it's about, for its sender's own bookkeeping: such as <c>announcement:12</c> or <c>backup-offer:7</c>.</summary>
    public string? Reference { get; init; }
}

/// <summary>
/// Sends email. The app's sender puts it in the queue (<see cref="Queue.QueueingEmailSender"/>), whose worker sends it through
/// the transport. Implementations throw when sending (or queueing) fails; callers decide whether that matters.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}
