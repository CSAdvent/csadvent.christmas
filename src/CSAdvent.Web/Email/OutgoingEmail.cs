namespace CSAdvent.Web.Email;

public sealed record EmailRecipient(string Address, string? Name = null);

/// <summary>A rendered email, ready for a transport: branded HTML with a plain-text alternative.</summary>
public sealed record OutgoingEmail(EmailRecipient To, string ReplyTo, string Subject, string Html, string Text);

/// <summary>Sends email. Implementations throw when sending fails; callers decide whether that matters.</summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}
