namespace CSAdvent.Web.Claims;

/// <summary>Tells the requester and the organizer about a new claim request.</summary>
public interface IClaimNotifier
{
    Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken);
}

/// <summary>Logs new claim requests, without personal details, until sending email is in place.</summary>
public sealed partial class LoggingClaimNotifier(ILogger<LoggingClaimNotifier> logger) : IClaimNotifier
{
    public Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken)
    {
        LogClaimRequested(claimRequestId, year, day, part);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Claim request {ClaimRequestId} for December {Day}, {Year}, part {Part}")]
    private partial void LogClaimRequested(int claimRequestId, int year, int day, int part);
}
