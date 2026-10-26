using System.Globalization;
using System.Text.Json;
using CSAdvent.Web.Content;

namespace CSAdvent.Web.DeadLinks;

/// <summary>What the archive said about a post: a copy, no copy, or nothing (it couldn't be asked).</summary>
/// <param name="Answered">Whether the archive answered. If it didn't, it's asked again sooner than a post it has no copy of.</param>
public sealed record ArchiveAnswer(bool Answered, string? CopyUrl)
{
    public static readonly ArchiveAnswer NoAnswer = new(false, null);

    public static readonly ArchiveAnswer NoCopy = new(true, null);

    public static ArchiveAnswer Copy(string url) => new(true, url);
}

/// <summary>Finds a saved copy of a post that's gone from its site.</summary>
public interface IArchiveLookup
{
    /// <param name="near">The day the copy should be closest to: the post's reveal, when it was surely up.</param>
    Task<ArchiveAnswer> FindAsync(string url, DateOnly near, CancellationToken cancellationToken);
}

/// <summary>
/// The Internet Archive's Wayback Machine, through its availability API: the snapshot closest to the post's reveal, if it saved
/// one that loaded (status 200). Only the post's public address is sent. Its own client, with <see cref="PublicAddresses"/>,
/// 20 seconds and no factory retries.
/// </summary>
public sealed partial class WaybackArchiveLookup(HttpClient http, ILogger<WaybackArchiveLookup> logger) : IArchiveLookup
{
    public const string Endpoint = "https://archive.org/wayback/available";

    /// <summary>Where every copy it finds is: readers are only ever sent to the Wayback Machine's own pages.</summary>
    public const string CopyPrefix = "https://web.archive.org/web/";

    public static HttpClient CreateClient()
    {
        var http = new HttpClient(PublicAddresses.CreateHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas/terms)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    public static string RequestUrl(string url, DateOnly near) =>
        string.Create(CultureInfo.InvariantCulture, $"{Endpoint}?url={Uri.EscapeDataString(url)}&timestamp={near:yyyyMMdd}");

    public async Task<ArchiveAnswer> FindAsync(string url, DateOnly near, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(RequestUrl(url, near), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnanswered((int)response.StatusCode);
                return ArchiveAnswer.NoAnswer;
            }

            return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception);
            return ArchiveAnswer.NoAnswer;
        }
    }

    /// <summary>
    /// Reads the availability API's answer: <c>archived_snapshots.closest</c>, if it's available and saved a page that loaded.
    /// Its address is the Wayback Machine's, over https; anything else is no copy.
    /// </summary>
    public static ArchiveAnswer Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("archived_snapshots", out var snapshots)
                || snapshots.ValueKind != JsonValueKind.Object
                || !snapshots.TryGetProperty("closest", out var closest)
                || closest.ValueKind != JsonValueKind.Object)
            {
                return ArchiveAnswer.NoCopy;
            }

            var available = closest.TryGetProperty("available", out var a) && a.ValueKind == JsonValueKind.True;
            var status = closest.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var copy = closest.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            if (!available || status != "200" || copy is null)
            {
                return ArchiveAnswer.NoCopy;
            }

            // It answers with http://; the same page is on https.
            if (copy.StartsWith("http://web.archive.org/", StringComparison.OrdinalIgnoreCase))
            {
                copy = "https://" + copy["http://".Length..];
            }

            return copy.StartsWith(CopyPrefix, StringComparison.OrdinalIgnoreCase) && copy.Length <= 1000 && Uri.TryCreate(copy, UriKind.Absolute, out _)
                ? ArchiveAnswer.Copy(copy)
                : ArchiveAnswer.NoCopy;
        }
        catch (JsonException)
        {
            return ArchiveAnswer.NoAnswer;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The Wayback Machine answered {Status}; it's asked again later.")]
    private partial void LogUnanswered(int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "The Wayback Machine couldn't be asked; it's asked again later.")]
    private partial void LogFailed(Exception exception);
}

/// <summary>
/// Development and the tests: a copy of any <see cref="SampleLinkProber"/> address with "archived" in it, near the given day,
/// and no copy of anything else. Never asks the Internet Archive.
/// </summary>
public sealed class SampleArchiveLookup : IArchiveLookup
{
    public Task<ArchiveAnswer> FindAsync(string url, DateOnly near, CancellationToken cancellationToken) =>
        Task.FromResult(Uri.TryCreate(url, UriKind.Absolute, out var uri) && SampleLinkProber.IsSample(uri) && uri.AbsolutePath.Contains("archived", StringComparison.Ordinal)
            ? ArchiveAnswer.Copy(string.Create(CultureInfo.InvariantCulture, $"{WaybackArchiveLookup.CopyPrefix}{near:yyyyMMdd}120000/{url}"))
            : ArchiveAnswer.NoCopy);
}
