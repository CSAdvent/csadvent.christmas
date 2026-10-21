using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using AzureEmailAddress = Azure.Communication.Email.EmailAddress;
using AzureEmailContent = Azure.Communication.Email.EmailContent;
using AzureEmailMessage = Azure.Communication.Email.EmailMessage;

namespace CSAdvent.Web.Email;

/// <summary>Sends through Azure Communication Services Email in production.</summary>
public sealed class AzureEmailSender(EmailClient client, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var message = new AzureEmailMessage(
            options.Value.FromAddress,
            new EmailRecipients([new AzureEmailAddress(email.To.Address, email.To.Name)]),
            new AzureEmailContent(email.Subject) { Html = email.Html, PlainText = email.Text });
        message.ReplyTo.Add(new AzureEmailAddress(email.ReplyTo));

        // Returns once the service accepts the message; delivery happens in the background.
        await client.SendAsync(WaitUntil.Started, message, cancellationToken);
    }
}
