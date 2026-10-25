namespace CSAdvent.Web.Email;

public sealed record EmailRecipient(string Address, string? Name = null);

/// <summary>
/// A rendered email, ready for a transport: branded HTML with a plain-text alternative. Emails people can opt out of carry
/// an <see cref="UnsubscribeUrl"/>, sent as the one-click <c>List-Unsubscribe</c> header (RFC 8058) as well as in the footer.
/// </summary>
/// <param name="Transactional">About something the recipient signed up for, so it's sent even if they unsubscribed (<see cref="EmailContent.Transactional"/>).</param>
public sealed record OutgoingEmail(EmailRecipient To, string ReplyTo, string Subject, string Html, string Text, string? UnsubscribeUrl = null, bool Transactional = false);

/// <summary>Sends email. Implementations throw when sending fails; callers decide whether that matters.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}
