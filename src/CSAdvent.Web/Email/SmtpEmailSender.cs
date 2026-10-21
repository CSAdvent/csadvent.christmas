using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CSAdvent.Web.Email;

/// <summary>Sends over SMTP with MailKit. Used with MailPit in development and the integration tests.</summary>
public sealed class SmtpEmailSender(EmailEndpoint endpoint, IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.Value.FromName, options.Value.FromAddress));
        message.To.Add(new MailboxAddress(email.To.Name ?? "", email.To.Address));
        message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text }.ToMessageBody();
        if (email.UnsubscribeUrl is { } unsubscribe)
        {
            message.Headers.Add("List-Unsubscribe", $"<{unsubscribe}>");
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        using var client = new SmtpClient();
        var security = endpoint.Uri.Scheme == "smtps" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(endpoint.Uri.Host, endpoint.Uri.Port, security, cancellationToken);
        if (endpoint.Credentials is { } credentials)
        {
            await client.AuthenticateAsync(credentials, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
