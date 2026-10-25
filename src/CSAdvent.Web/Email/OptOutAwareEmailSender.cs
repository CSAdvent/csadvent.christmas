using Npgsql;

namespace CSAdvent.Web.Email;

/// <summary>
/// Wraps the transport so nothing is ever sent to an address that unsubscribed from C# Advent's emails (<see cref="EmailOptOuts"/>),
/// whichever feature sends it. A skipped email counts as sent, so nothing retries it. The organizers' inbox is never skipped.
/// </summary>
public sealed partial class OptOutAwareEmailSender(IEmailSender inner, EmailOptOutLinks links, IServiceScopeFactory scopeFactory, ILogger<OptOutAwareEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        if (!links.IsOrganizers(email.To.Address) && await IsOptedOutAsync(email.To.Address, cancellationToken))
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
            return await scope.ServiceProvider.GetRequiredService<EmailOptOuts>().IsOptedOutAsync(address, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // A new revision starts before the deploy runs the migrations, and nobody can have unsubscribed before then.
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Didn't send an email: its recipient unsubscribed from C# Advent's emails")]
    private partial void LogSkipped();
}
