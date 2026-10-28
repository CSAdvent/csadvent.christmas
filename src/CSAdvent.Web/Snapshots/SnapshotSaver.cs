using System.Globalization;
using System.Net;
using System.Text.Json;
using CSAdvent.Web.Content;
using CSAdvent.Web.DeadLinks;

namespace CSAdvent.Web.Snapshots;

/// <summary>Where a capture stands, by the Wayback Machine's answer.</summary>
public enum CaptureState
{
    /// <summary>It's saving the page: ask again about <see cref="CaptureAnswer.JobId"/>.</summary>
    Pending,

    /// <summary>It saved the page, at <see cref="CaptureAnswer.SnapshotUrl"/>.</summary>
    Saved,

    /// <summary>It couldn't save the page (it didn't load, or the site blocks the archive, say). Counts as a try.</summary>
    Failed,

    /// <summary>It couldn't be asked, or didn't say (it's down or busy, or the request timed out). Doesn't count as a try.</summary>
    NoAnswer,
}

/// <summary>What the Wayback Machine said about a capture.</summary>
/// <param name="Detail">What it said, for the organizers, when it didn't save it.</param>
public sealed record CaptureAnswer(CaptureState State, string? JobId = null, string? SnapshotUrl = null, DateTimeOffset? SavedAt = null, string? Detail = null)
{
    public static CaptureAnswer Pending(string jobId) => new(CaptureState.Pending, JobId: jobId);

    public static CaptureAnswer Saved(string snapshotUrl, DateTimeOffset savedAt) => new(CaptureState.Saved, SnapshotUrl: snapshotUrl, SavedAt: savedAt);

    public static CaptureAnswer Failed(string detail) => new(CaptureState.Failed, Detail: detail);

    public static CaptureAnswer NoAnswer(string detail) => new(CaptureState.NoAnswer, Detail: detail);
}

/// <summary>A capture we asked for: its job, and what it's of.</summary>
public sealed record CaptureJob(string JobId, string PostUrl, DateTimeOffset RequestedAt);

/// <summary>Asks the Wayback Machine to save a post, then how it went.</summary>
public interface ISnapshotSaver
{
    /// <summary>Asks for a snapshot of the post. Usually <see cref="CaptureState.Pending"/>, with the job to ask about.</summary>
    Task<CaptureAnswer> StartAsync(string url, CancellationToken cancellationToken);

    /// <summary>How the capture is going.</summary>
    Task<CaptureAnswer> StatusAsync(CaptureJob job, CancellationToken cancellationToken);
}

/// <summary>
/// The Internet Archive's Save Page Now API (SPN2), with an archive.org account's S3 keys: a POST to
/// <see cref="StartEndpoint"/> starts a capture and answers with its job, and <see cref="StatusEndpoint"/> says how it went.
/// Only the post's public address is sent. Its own client, with <see cref="PublicAddresses"/>, 60 seconds and no factory retries.
/// </summary>
public sealed partial class WaybackSnapshotSaver(HttpClient http, ILogger<WaybackSnapshotSaver> logger) : ISnapshotSaver
{
    public const string StartEndpoint = "https://web.archive.org/save";

    public const string StatusEndpoint = "https://web.archive.org/save/status/";

    private const int MaxDetail = 500;

    public static HttpClient CreateClient(string? keys) =>
        Configure(new HttpClient(PublicAddresses.CreateHandler()) { Timeout = TimeSpan.FromSeconds(60) }, keys);

    /// <summary>Our user agent, JSON answers, and the account's keys, if there are any.</summary>
    public static HttpClient Configure(HttpClient http, string? keys)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas/terms)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        if (!string.IsNullOrWhiteSpace(keys))
        {
            // SPN2's own scheme: "LOW accesskey:secret".
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"LOW {keys.Trim()}");
        }

        return http;
    }

    public async Task<CaptureAnswer> StartAsync(string url, CancellationToken cancellationToken)
    {
        // Skipping the "first capture" check makes it quicker; it only decides whether the archive emails the account.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["url"] = url, ["skip_first_archive"] = "1" });
        return await SendAsync(() => http.PostAsync(StartEndpoint, form, cancellationToken), ParseStart, cancellationToken);
    }

    public Task<CaptureAnswer> StatusAsync(CaptureJob job, CancellationToken cancellationToken) =>
        SendAsync(() => http.GetAsync(StatusEndpoint + Uri.EscapeDataString(job.JobId), cancellationToken), ParseStatus, cancellationToken);

    private async Task<CaptureAnswer> SendAsync(Func<Task<HttpResponseMessage>> send, Func<string, CaptureAnswer> parse, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                LogRefusedKeys((int)response.StatusCode);
                return CaptureAnswer.NoAnswer("The Wayback Machine refused our archive.org keys.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                LogUnanswered((int)response.StatusCode);
                return CaptureAnswer.NoAnswer($"The Wayback Machine answered {(int)response.StatusCode}.");
            }

            return parse(body);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
            return CaptureAnswer.NoAnswer("The Wayback Machine couldn't be reached.");
        }
    }

    /// <summary>
    /// Reads Save Page Now's answer to a capture request: its <c>job_id</c>, or an error (<c>status: "error"</c>) about the page.
    /// Anything else, such as a <c>message</c> that the account is busy or the page was saved minutes ago, is no answer.
    /// </summary>
    public static CaptureAnswer ParseStart(string json)
    {
        if (Read(json) is not { } root)
        {
            return CaptureAnswer.NoAnswer("The Wayback Machine's answer wasn't readable.");
        }

        if (String(root, "job_id") is { Length: > 0 and <= 100 } jobId)
        {
            return CaptureAnswer.Pending(jobId);
        }

        return String(root, "status") == "error"
            ? CaptureAnswer.Failed(ErrorDetail(root))
            : CaptureAnswer.NoAnswer(Trim(String(root, "message") is { Length: > 0 } message ? $"The Wayback Machine said: {message}" : "The Wayback Machine didn't start a capture."));
    }

    /// <summary>
    /// Reads a capture's status: <c>pending</c>; <c>success</c>, with the page's <c>timestamp</c> and <c>original_url</c> (after
    /// redirects), which make its address on the Wayback Machine; or <c>error</c>, with what went wrong.
    /// </summary>
    public static CaptureAnswer ParseStatus(string json)
    {
        if (Read(json) is not { } root)
        {
            return CaptureAnswer.NoAnswer("The Wayback Machine's answer wasn't readable.");
        }

        switch (String(root, "status"))
        {
            case "pending":
                return CaptureAnswer.Pending(String(root, "job_id") ?? "");
            case "error":
                return CaptureAnswer.Failed(ErrorDetail(root));
            case "success"
                when String(root, "timestamp") is { Length: 14 } timestamp
                    && DateTimeOffset.TryParseExact(timestamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var savedAt)
                    && String(root, "original_url") is { } original
                    && Uri.TryCreate(original, UriKind.Absolute, out var originalUri)
                    && (originalUri.Scheme == Uri.UriSchemeHttps || originalUri.Scheme == Uri.UriSchemeHttp):
                var snapshot = $"{WaybackArchiveLookup.CopyPrefix}{timestamp}/{original}";
                return snapshot.Length <= 1000
                    ? CaptureAnswer.Saved(snapshot, savedAt)
                    : CaptureAnswer.Failed("The saved page's address is too long to keep.");
            default:
                return CaptureAnswer.NoAnswer("The Wayback Machine didn't say how the capture went.");
        }
    }

    private static JsonElement? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ErrorDetail(JsonElement root)
    {
        var message = String(root, "message") is { Length: > 0 } m ? m.Trim() : "It couldn't save the page.";
        return Trim(String(root, "status_ext") is { Length: > 0 } code ? $"The Wayback Machine said: {message} ({code})" : $"The Wayback Machine said: {message}");
    }

    private static string Trim(string detail) => detail.Length <= MaxDetail ? detail : detail[..(MaxDetail - 1)] + "…";

    [LoggerMessage(Level = LogLevel.Error, Message = "The Wayback Machine refused our archive.org keys ({Status}). Check the wayback-keys secret.")]
    private partial void LogRefusedKeys(int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Wayback Machine answered {Status}; it's asked again later.")]
    private partial void LogUnanswered(int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Wayback Machine couldn't be asked; it's asked again later.")]
    private partial void LogFailed(Exception exception);
}

/// <summary>
/// Development and the tests: never asks the Internet Archive. A post whose address has "busy" in it gets no answer, one with
/// "slow" is never done capturing, one with "gone" can't be saved, and anything else is "saved" when it was asked for.
/// </summary>
public sealed class SampleSnapshotSaver : ISnapshotSaver
{
    public const string JobId = "sample";

    public Task<CaptureAnswer> StartAsync(string url, CancellationToken cancellationToken) =>
        Task.FromResult(url.Contains("busy", StringComparison.Ordinal)
            ? CaptureAnswer.NoAnswer("The Wayback Machine answered 503.")
            : CaptureAnswer.Pending(JobId));

    public Task<CaptureAnswer> StatusAsync(CaptureJob job, CancellationToken cancellationToken) =>
        Task.FromResult(
            job.PostUrl.Contains("slow", StringComparison.Ordinal) ? CaptureAnswer.Pending(job.JobId)
            : job.PostUrl.Contains("gone", StringComparison.Ordinal) ? CaptureAnswer.Failed("The Wayback Machine said: The page wasn't found. (error:not-found)")
            : CaptureAnswer.Saved(string.Create(CultureInfo.InvariantCulture, $"{WaybackArchiveLookup.CopyPrefix}{job.RequestedAt.UtcDateTime:yyyyMMddHHmmss}/{job.PostUrl}"), job.RequestedAt));
}
