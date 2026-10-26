using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry.Instrumentation.Http;

namespace CSAdvent.Web.Auth;

/// <summary>
/// Keeps GitHub logins out of the telemetry (#168). The HTTP client tracing records every outgoing request's full address,
/// which Application Insights keeps, and looking up an account (<c>GitHubUsers</c>) or an organizer's photo
/// (<c>OrganizerPhotos</c>) puts a login in it. Logins are public, but the logs carry no personal details, so the login is
/// replaced with <see cref="Redacted"/> before the request's span is exported.
/// </summary>
public static partial class GitHubTelemetry
{
    /// <summary>What a login becomes, like the query values the tracing already redacts.</summary>
    public const string Redacted = "Redacted";

    /// <summary>The tags the tracing keeps the address in: the current name, and the one older instrumentation used.</summary>
    private static readonly string[] UrlTags = ["url.full", "http.url"];

    /// <summary>Redacts the address once the request is done, after the tracing has recorded it.</summary>
    public static void Configure(HttpClientTraceInstrumentationOptions options)
    {
        options.EnrichWithHttpResponseMessage = (activity, _) => Redact(activity);
        options.EnrichWithException = (activity, _) => Redact(activity);
    }

    /// <summary>The address with the login in it replaced, or null if it has none.</summary>
    public static string? Redact(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.Host.ToLowerInvariant() switch
        {
            "api.github.com" => UsersPath().Replace(uri.AbsolutePath, $"/users/{Redacted}"),
            "github.com" or "www.github.com" => PhotoPath().Replace(uri.AbsolutePath, $"/{Redacted}.png"),
            _ => uri.AbsolutePath,
        };

        return path == uri.AbsolutePath ? null : uri.GetLeftPart(UriPartial.Authority) + path + uri.Query;
    }

    private static void Redact(Activity activity)
    {
        foreach (var tag in UrlTags)
        {
            if (activity.GetTagItem(tag) is string url && Redact(url) is { } redacted)
            {
                activity.SetTag(tag, redacted);
            }
        }
    }

    // GitHub's API: "/users/{login}" and "/users/{login}/social_accounts".
    [GeneratedRegex("^/users/[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex UsersPath();

    // A profile photo: "/{login}.png".
    [GeneratedRegex(@"^/[^/]+\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex PhotoPath();
}
