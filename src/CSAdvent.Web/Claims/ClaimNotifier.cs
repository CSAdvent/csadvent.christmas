namespace CSAdvent.Web.Claims;

/// <summary>Tells contributors and organizers about claim requests.</summary>
public interface IClaimNotifier
{
    /// <summary>A contributor asked for a spot: confirm to them and tell the organizers.</summary>
    Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken);

    /// <summary>An organizer approved a request into a spot: tell the contributor.</summary>
    Task ClaimApprovedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken);
}

/// <summary>Logs new claim requests, without personal details, until sending email is in place.</summary>
public sealed partial class LoggingClaimNotifier(ILogger<LoggingClaimNotifier> logger) : IClaimNotifier
{
    public Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken)
    {
        LogClaimRequested(claimRequestId, year, day, part);
        return Task.CompletedTask;
    }

    public Task ClaimApprovedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken)
    {
        LogClaimApproved(claimRequestId, year, day, part);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Claim request {ClaimRequestId} for December {Day}, {Year}, part {Part}")]
    private partial void LogClaimRequested(int claimRequestId, int year, int day, int part);

    [LoggerMessage(Level = LogLevel.Information, Message = "Claim request {ClaimRequestId} approved into December {Day}, {Year}, part {Part}")]
    private partial void LogClaimApproved(int claimRequestId, int year, int day, int part);
}
