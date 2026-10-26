using CSAdvent.Web.Data;

namespace CSAdvent.Web.Email;

/// <summary>
/// Wraps the transport so no optional email is ever sent to an address that unsubscribed (<see cref="IEmailOptOutList"/>),
/// whichever feature sends it. A skipped email counts as sent, so nothing retries it. Emails about a spot, request or backup
/// offer the recipient signed up for (<see cref="OutgoingEmail.Transactional"/>) and the organizers' inbox are never skipped.
/// </summary>
public sealed partial class OptOutAwareEmailSender(IEmailSender inner, EmailOptOutLinks links, IServiceScopeFactory scopeFactory, ILogger<OptOutAwareEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        if (!email.Transactional && !links.IsOrganizers(email.To.Address) && await IsOptedOutAsync(email.To.Address, cancellationToken))
        {
            LogSkipped();
            return;
        }

        await inner.SendAsync(email, cancellationToken);
    }

    private async Task<bool> IsOptedOutAsync(string address, CancellationToken cancellationToken)
    {
        // The sender is shared by the whole app, so the list is read in a scope of its own.
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IEmailOptOutList>().IsOptedOutAsync(address, cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTable(exception))
        {
            // A new revision starts before the deploy runs the migrations, and nobody can have unsubscribed before then. Only
            // the table: with a column missing, people may well have, so the send fails and the email queue tries it again.
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Didn't send an optional email: its recipient unsubscribed")]
    private partial void LogSkipped();
}
