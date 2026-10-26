using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Posts to C# Advent's X account with <c>POST /2/tweets</c>, signed with OAuth 1.0a as the account. X builds the link card
/// itself from the entry page's preview tags. It has no idempotency key. It refuses a post identical to a recent one ("duplicate
/// content"), but X doesn't document how recent, so that isn't relied on: a try that may have gone out waits for an organizer
/// to check (#154).
/// </summary>
public sealed class XPublisher(HttpClient http, string? account, XCredentials credentials, TimeProvider timeProvider) : ISocialPublisher
{
    public SocialPlatform Platform => SocialPlatform.X;

    public async Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "tweets");
        request.Content = JsonContent.Create(new { text = publication.Announcement.Text });

        // X answers with the new post's ID. /i/web/status/{id} finds a post without knowing whose it is.
        return (await http.SendPostAsync<Created>(request, Platform, cancellationToken))?.Data?.Id is { Length: > 0 } id
            ? account is { Length: > 0 } ? $"https://x.com/{account}/status/{id}" : $"https://x.com/i/web/status/{id}"
            : null;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "users/me");
        using var response = await http.SendAsync(request, cancellationToken);
        await SocialPublishException.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// The OAuth 1.0a Authorization header's value (after <c>OAuth</c>) for a request, signed with HMAC-SHA1. The signature
    /// covers the method, the address with its query, and any form fields; a JSON body isn't part of it.
    /// </summary>
    public static string Authorization(
        HttpMethod method,
        Uri url,
        IEnumerable<KeyValuePair<string, string>> formFields,
        XCredentials credentials,
        DateTimeOffset now,
        string nonce)
    {
        var oauth = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = credentials.ApiKey ?? "",
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["oauth_token"] = credentials.AccessToken ?? "",
            ["oauth_version"] = "1.0",
        };

        var query = url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => KeyValuePair.Create(Decode(parts[0]), parts.Length > 1 ? Decode(parts[1]) : ""));

        var parameters = string.Join("&", oauth.Concat(query).Concat(formFields)
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));
        var signatureBase = $"{method.Method.ToUpperInvariant()}&{Encode(url.GetLeftPart(UriPartial.Path))}&{Encode(parameters)}";
        var key = $"{Encode(credentials.ApiKeySecret ?? "")}&{Encode(credentials.AccessTokenSecret ?? "")}";

        oauth["oauth_signature"] = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(signatureBase)));
        return string.Join(", ", oauth.Select(p => $"{Encode(p.Key)}=\"{Encode(p.Value)}\""));
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var url = new Uri($"https://api.x.com/2/{path}");
        var authorization = Authorization(method, url, [], credentials, timeProvider.GetUtcNow(), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        return new(method, url) { Headers = { Authorization = new AuthenticationHeaderValue("OAuth", authorization) } };
    }

    // RFC 3986, which OAuth 1.0a uses: everything but letters, digits and "-._~" is percent-encoded.
    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private sealed record Created(CreatedPost? Data);

    private sealed record CreatedPost(string? Id);
}
