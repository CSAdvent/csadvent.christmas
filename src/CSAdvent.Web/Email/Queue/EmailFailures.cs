using System.Globalization;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Azure;
using MailKit.Net.Smtp;

namespace CSAdvent.Web.Email.Queue;

/// <summary>What a failed send means for the queue.</summary>
public enum EmailFailureKind
{
    /// <summary>Try this email again later, with backoff.</summary>
    Failed,

    /// <summary>The email service has had enough for now (429, or SMTP's 421): stop sending, and don't count it against the email.</summary>
    Throttled,
}

/// <param name="Description">Short, and without addresses, for <c>last_error</c> and the logs.</param>
/// <param name="RetryAfter">For throttling, how long the email service asked us to wait, if it said.</param>
public sealed record EmailFailure(EmailFailureKind Kind, string Description, TimeSpan? RetryAfter = null);

/// <summary>
/// Makes sense of the transports' exceptions. Exception messages can quote the recipient's address, so the description is
/// built from status codes and exception types instead, and never logged with the exception itself.
/// </summary>
public static partial class EmailFailures
{
    /// <summary>The longest Retry-After the queue honors, in case a service asks for something absurd.</summary>
    private static readonly TimeSpan LongestPause = TimeSpan.FromHours(1);

    public static EmailFailure Classify(Exception exception) => exception switch
    {
        RequestFailedException { Status: 429 } azure => new(EmailFailureKind.Throttled, Describe(azure), RetryAfter(azure)),
        SmtpCommandException { StatusCode: SmtpStatusCode.ServiceNotAvailable } smtp => new(EmailFailureKind.Throttled, Describe(smtp)),
        _ => new(EmailFailureKind.Failed, Describe(exception)),
    };

    public static string Describe(Exception exception)
    {
        var description = exception switch
        {
            RequestFailedException azure => string.IsNullOrEmpty(azure.ErrorCode)
                ? $"The email service answered {azure.Status}"
                : $"The email service answered {azure.Status} ({azure.ErrorCode})",
            SmtpCommandException smtp => $"The SMTP server answered {(int)smtp.StatusCode} ({smtp.ErrorCode})",
            SocketException socket => $"Couldn't reach the email service ({socket.SocketErrorCode})",
            OperationCanceledException => "Sending took too long",
            _ => $"Sending failed ({exception.GetType().Name})",
        };

        // Error codes come from the server, so make sure no address slips through.
        var safe = Address().Replace(description, "[address]");
        return safe.Length <= 300 ? safe : safe[..300];
    }

    /// <summary>
    /// The email service's Retry-After: seconds or an HTTP date, or Azure's <c>retry-after-ms</c>. Never more than an hour.
    /// </summary>
    private static TimeSpan? RetryAfter(RequestFailedException exception)
    {
        if (exception.GetRawResponse() is not { } response)
        {
            return null;
        }

        TimeSpan? wait = null;
        if (response.Headers.TryGetValue("retry-after-ms", out var milliseconds) && double.TryParse(milliseconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
        {
            wait = TimeSpan.FromMilliseconds(ms);
        }
        else if (response.Headers.TryGetValue("Retry-After", out var retryAfter))
        {
            if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                wait = TimeSpan.FromSeconds(seconds);
            }
            else if (DateTimeOffset.TryParse(retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                wait = at - DateTimeOffset.UtcNow;
            }
        }

        return wait is { } value && value > TimeSpan.Zero ? (value < LongestPause ? value : LongestPause) : null;
    }

    [GeneratedRegex(@"[^\s<>""'()\[\]]+@[^\s<>""'()\[\]]+")]
    private static partial Regex Address();
}
