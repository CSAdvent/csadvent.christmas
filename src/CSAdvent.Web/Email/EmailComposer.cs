using CSAdvent.Web.Content;
using CSAdvent.Web.Email.Queue;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email;

/// <summary>
/// Turns <see cref="EmailContent"/> into an <see cref="OutgoingEmail"/>: the branded HTML and its plain-text alternative.
/// Every optional email to a person has an unsubscribe link: its own (the daily email's, an announcement's), or else the one
/// that stops all of C# Advent's optional emails (<see cref="EmailOptOutLinks"/>). Emails about a spot, request or backup
/// offer the recipient signed up for (<see cref="EmailContent.Transactional"/>) and the organizers' inbox go without.
/// <see cref="TrySendAsync(EmailContent, EmailRecipient, string?, string, CancellationToken)"/> composes one and queues it.
/// </summary>
public sealed partial class EmailComposer(
    IServiceScopeFactory scopeFactory,
    ILoggerFactory loggerFactory,
    IOptions<SiteOptions> site,
    EmailOptOutLinks optOutLinks,
    IEmailSender sender)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<EmailComposer>();

    /// <summary>
    /// Composes the email and queues it (<see cref="IEmailSender"/>). Returns false if it couldn't be, which only happens when
    /// the database fails: that's logged with the email's kind and what it's about, never the address (#168), and the caller
    /// undoes whatever it recorded so a later run tries again. A cancelled send throws instead.
    /// </summary>
    /// <param name="about">What the email is about, for the log, such as <c>slot 12</c>. Never an address or a name.</param>
    public Task<bool> TrySendAsync(EmailContent content, EmailRecipient to, string about, CancellationToken cancellationToken = default) =>
        TrySendAsync(content, to, replyTo: null, about, cancellationToken);

    /// <summary>
    /// Composes the email and queues it, with replies going to <paramref name="replyTo"/> (the organizers when it's null).
    /// Returns false if it couldn't be queued, which is logged without the address.
    /// </summary>
    /// <param name="about">What the email is about, for the log, such as <c>claim request 7</c>. Never an address or a name.</param>
    public async Task<bool> TrySendAsync(EmailContent content, EmailRecipient to, string? replyTo, string about, CancellationToken cancellationToken = default)
    {
        try
        {
            await sender.SendAsync(await ComposeAsync(content, to, replyTo), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotQueued(exception, content.Kind ?? QueueingEmailSender.UnknownKind, about);
            return false;
        }
    }

    /// <summary>Composes an email. Replies go to the organizers unless <paramref name="replyTo"/> says otherwise.</summary>
    public async Task<OutgoingEmail> ComposeAsync(EmailContent content, EmailRecipient to, string? replyTo = null)
    {
        var unsubscribe = content.Transactional
            ? null
            : content.UnsubscribeUrl ?? (optOutLinks.IsOrganizers(to.Address) ? null : optOutLinks.CreateLink(to.Address));
        var withLink = content with { UnsubscribeUrl = unsubscribe };
        return new(
            to,
            replyTo ?? site.Value.ContactEmail,
            EmailBlocks.SingleLine(withLink.Subject),
            await RenderHtmlAsync(withLink),
            RenderText(withLink),
            unsubscribe,
            content.Transactional)
        {
            Kind = content.Kind,
            Priority = content.Priority,
            SendBefore = content.SendBefore,
            Reference = content.Reference,
        };
    }

    public async Task<string> RenderHtmlAsync(EmailContent content)
    {
        // The renderer resolves scoped services, so it can't run on the root provider.
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BrandedEmail.Content)] = content,
                [nameof(BrandedEmail.BaseUrl)] = site.Value.BaseUrl,
            });
            var output = await renderer.RenderComponentAsync<BrandedEmail>(parameters);
            return output.ToHtmlString();
        });
    }

    public string RenderText(EmailContent content) => EmailText.Render(content, site.Value.BaseUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the {Kind} email ({About})")]
    private partial void LogNotQueued(Exception exception, string kind, string about);
}
