using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CSAdvent.Web.Content;

namespace CSAdvent.Web.DeadLinks;

/// <summary>What a post's address did when it was checked.</summary>
public enum LinkOutcome
{
    /// <summary>The page loaded.</summary>
    Loaded,

    /// <summary>It's not there: a 404 or 410, a site whose address doesn't exist any more, a site that's down, or a redirect home.</summary>
    Failed,

    /// <summary>The site turned the check away (403, 429 and the like), so there's no telling.</summary>
    Refused,
}

/// <summary>A post's address, checked: what it did, and in words for the organizers.</summary>
public sealed record LinkProbe(LinkOutcome Outcome, string Detail)
{
    public static LinkProbe Loaded(string detail = "The page loaded.") => new(LinkOutcome.Loaded, detail);

    public static LinkProbe Failed(string detail) => new(LinkOutcome.Failed, detail);

    public static LinkProbe Refused(string detail) => new(LinkOutcome.Refused, detail);
}

/// <summary>Checks whether a post is still at its address.</summary>
public interface ILinkProber
{
    Task<LinkProbe> ProbeAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>How a response or a failure counts, apart from the fetching, so it can be tested.</summary>
public static class LinkProbes
{
    /// <summary>
    /// What a response means. A 404 or 410 is gone, and so is a post that now redirects to its site's home page, which is what
    /// many blogs do with posts they've removed. Other refusals (401, 403, 429, a bot wall) say nothing about the post, and a
    /// 5xx or a redirect that never ends is the site being down.
    /// </summary>
    public static LinkProbe FromResponse(int status, string? reason, Uri requested, Uri final)
    {
        var answered = $"{status} {reason}".Trim();
        return status switch
        {
            >= 200 and < 300 when IsRedirectHome(requested, final) => LinkProbe.Failed($"It now redirects to the home page of {final.Host}."),
            >= 200 and < 300 => LinkProbe.Loaded(),
            404 or 410 => LinkProbe.Failed($"The page answered {answered}."),
            >= 300 and < 400 => LinkProbe.Failed("It redirects too many times."),
            >= 400 and < 500 => LinkProbe.Refused($"The site turned the check away ({answered})."),
            _ => LinkProbe.Failed($"The site answered {answered}."),
        };
    }

    /// <summary>Why a request didn't get an answer: a site whose address no longer exists, a bad certificate, or no answer.</summary>
    public static LinkProbe FromException(Exception exception, Uri url)
    {
        if (Find<SocketException>(exception) is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData })
        {
            return LinkProbe.Failed($"The site's address, {url.Host}, doesn't exist any more.");
        }

        if (Find<AuthenticationException>(exception) is not null)
        {
            return LinkProbe.Failed("The site's certificate isn't valid.");
        }

        return exception is TaskCanceledException or TimeoutException
            ? LinkProbe.Failed("The site didn't answer in time.")
            : LinkProbe.Failed("The site couldn't be reached.");
    }

    /// <summary>A post that now lands on its site's home page (or another site's), rather than on a page of its own.</summary>
    public static bool IsRedirectHome(Uri requested, Uri final) =>
        IsHome(final) && !IsHome(requested);

    /// <summary>The site a post is on, for spacing out checks: its host, without <c>www.</c>.</summary>
    public static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant()
            : url;

    private static bool IsHome(Uri url) => url.AbsolutePath is "" or "/" && url.Query.Length <= 1;

    private static T? Find<T>(Exception? exception) where T : Exception => exception switch
    {
        null => null,
        T found => found,
        _ => Find<T>(exception.InnerException),
    };
}

/// <summary>
/// Checks posts on the web with a client of its own, like the blurb check's post fetcher: authors typed these addresses, so it
/// only connects to public addresses (<see cref="PublicAddresses"/>, checked on every redirect too), gives up after 20
/// seconds, and has no factory retries (the post is checked again another day). It asks for the page and reads only the
/// answer's status, never the page itself.
/// </summary>
public sealed partial class WebLinkProber(HttpClient http, ILogger<WebLinkProber> logger) : ILinkProber
{
    public static HttpClient CreateClient()
    {
        var http = new HttpClient(PublicAddresses.CreateHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas/terms)");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return http;
    }

    public async Task<LinkProbe> ProbeAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            // Only the headers: the body is never read, and disposing the response drops the connection's download.
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return LinkProbes.FromResponse((int)response.StatusCode, response.ReasonPhrase, url, response.RequestMessage?.RequestUri ?? url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            LogNotReached(exception, url.Host);
            return LinkProbes.FromException(exception, url);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The dead-link check couldn't reach {Host}.")]
    private partial void LogNotReached(Exception exception, string host);
}

/// <summary>
/// Development and the tests (<see cref="LinkCheckOptions.SamplePages"/>): answers for <c>links.csadvent.test</c> and its
/// subdomains by the address's first path segment, and never fetches anything. Any other address counts as loaded, so the real
/// archive's posts look fine locally.
/// <list type="bullet">
/// <item><c>/working…</c> loads.</item>
/// <item><c>/refused…</c> is turned away (403).</item>
/// <item><c>/down…</c> can't be reached.</item>
/// <item><c>/home…</c> redirects to the site's home page.</item>
/// <item>Anything else (<c>/gone</c>, <c>/gone-archived</c>…) is a 404.</item>
/// </list>
/// </summary>
public sealed class SampleLinkProber : ILinkProber
{
    public const string Host = "links.csadvent.test";

    public static bool IsSample(Uri url) =>
        url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith($".{Host}", StringComparison.OrdinalIgnoreCase);

    public Task<LinkProbe> ProbeAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!IsSample(url))
        {
            return Task.FromResult(LinkProbe.Loaded($"Not checked: only {Host} is checked here."));
        }

        var segment = url.AbsolutePath.Trim('/').Split('/')[0];
        return Task.FromResult(segment switch
        {
            _ when segment.StartsWith("working", StringComparison.Ordinal) => LinkProbes.FromResponse(200, "OK", url, url),
            _ when segment.StartsWith("refused", StringComparison.Ordinal) => LinkProbes.FromResponse(403, "Forbidden", url, url),
            _ when segment.StartsWith("down", StringComparison.Ordinal) => LinkProbes.FromException(new HttpRequestException("Connection refused"), url),
            _ when segment.StartsWith("home", StringComparison.Ordinal) => LinkProbes.FromResponse(200, "OK", url, new Uri(url, "/")),
            _ => LinkProbes.FromResponse((int)HttpStatusCode.NotFound, "Not Found", url, url),
        });
    }
}
