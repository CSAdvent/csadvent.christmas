using CSAdvent.Web.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email;

/// <summary>Turns <see cref="EmailContent"/> into an <see cref="OutgoingEmail"/>: the branded HTML and its plain-text alternative.</summary>
public sealed class EmailComposer(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, IOptions<SiteOptions> site)
{
    /// <summary>Composes an email. Replies go to the organizers unless <paramref name="replyTo"/> says otherwise.</summary>
    public async Task<OutgoingEmail> ComposeAsync(EmailContent content, EmailRecipient to, string? replyTo = null) =>
        new(to, replyTo ?? site.Value.ContactEmail, EmailBlocks.SingleLine(content.Subject), await RenderHtmlAsync(content), RenderText(content));

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
}
