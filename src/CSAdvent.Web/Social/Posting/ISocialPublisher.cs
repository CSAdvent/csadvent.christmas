using System.Net.Http.Json;
using System.Text.Json;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Social.Posting;

/// <summary>An announcement, ready to post.</summary>
/// <param name="Description">What a link card says under the title, such as "By Ada Lovelace, for C# Advent 2026: December 6, post 2."</param>
/// <param name="Card">The entry's share card (a JPEG), for platforms where we build the link card ourselves.</param>
public sealed record SocialPublication(int Year, int Day, int Part, string Title, string Description, FormattedAnnouncement Announcement, byte[] Card)
{
    /// <summary>Instead of the entry's key: a test post's own, so each one goes out.</summary>
    public string? Key { get; init; }

    /// <summary>The same for every try at this announcement, so a platform that supports one never posts it twice.</summary>
    public string IdempotencyKey => Key ?? $"csadvent-{Year}-{Day}-{Part}-{Announcement.Platform}".ToLowerInvariant();
}

/// <summary>Posts announcements to one platform, as C# Advent's account.</summary>
public interface ISocialPublisher
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// Whether trying a post again can never post it twice, even after a try that may have gone out
    /// (<see cref="SocialPublishException.MayHavePosted"/>). Only Bluesky's can: each try looks for the post an earlier one made.
    /// </summary>
    bool RetriesAreSafe => false;

    /// <summary>
    /// Posts it. Returns the post's address, if the platform gives one. Throws <see cref="SocialPublishException"/> if the
    /// platform refuses, or if it may have posted it without saying so (<see cref="SocialPublishException.MayHavePosted"/>).
    /// </summary>
    Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken);

    /// <summary>Signs in, or makes the cheapest authenticated call, so a revoked or expired credential shows up before it's needed.</summary>
    Task CheckAsync(CancellationToken cancellationToken);
}

/// <summary>A platform refused a request, or didn't answer it. The message is the platform's own where it gave one, for the organizers.</summary>
public sealed class SocialPublishException(string message, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>
    /// The request that makes the post had gone when it failed: it timed out, its connection dropped, or the platform answered
    /// with a server error. The platform may have posted it anyway, so trying again could post it twice (#154).
    /// </summary>
    public bool MayHavePosted { get; init; }

    /// <summary>The platform's error message from a failed response, or its status code if it didn't give one.</summary>
    public static async Task<SocialPublishException> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ErrorMessage(body);
        var status = $"{(int)response.StatusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()}";
        return new SocialPublishException(detail is null ? status : $"{status}: {detail}");
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await FromAsync(response, cancellationToken);
        }
    }

    /// <summary>
    /// The message in an error body: Mastodon's <c>{"error": "…"}</c>, Bluesky's <c>{"error": "…", "message": "…"}</c>,
    /// Facebook's <c>{"error": {"message": "…"}}</c>, or X's <c>{"title": "…", "detail": "…"}</c> or
    /// <c>{"errors": [{"message": "…"}]}</c>.
    /// </summary>
    public static string? ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind is JsonValueKind.String)
            {
                return message.GetString();
            }

            if (root.TryGetProperty("error", out var error))
            {
                return error.ValueKind switch
                {
                    JsonValueKind.String => error.GetString(),
                    JsonValueKind.Object when error.TryGetProperty("message", out var inner) && inner.ValueKind is JsonValueKind.String => inner.GetString(),
                    _ => null,
                };
            }

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind is JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind is JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].ValueKind is JsonValueKind.Object && errors[0].TryGetProperty("message", out var first) && first.ValueKind is JsonValueKind.String)
            {
                return first.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Sending the request that makes a post, so what a failure means is decided in one place (#154).</summary>
public static class SocialPostRequests
{
    /// <summary>
    /// Sends the request that makes the post and reads the platform's answer as <typeparamref name="T"/>.
    /// <list type="bullet">
    /// <item>A refusal (4xx), or a request that never left (the address didn't resolve, or the connection couldn't be made),
    /// throws <see cref="SocialPublishException"/>: it didn't go out.</item>
    /// <item>Once the request has gone, a timeout, a dropped connection or a server error (5xx) can come after the platform
    /// posted it, so those throw one with <see cref="SocialPublishException.MayHavePosted"/>.</item>
    /// <item>A success whose answer can't be read is still a success, without the answer (null).</item>
    /// </list>
    /// </summary>
    public static async Task<T?> SendPostAsync<T>(this HttpClient http, HttpRequestMessage request, SocialPlatform platform, CancellationToken cancellationToken)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SocialPublishException($"{platform} didn't answer in time.", exception) { MayHavePosted = true };
        }
        catch (HttpRequestException exception) when (!NeverLeft(exception))
        {
            throw new SocialPublishException($"The connection to {platform} dropped before it answered: {exception.Message}", exception) { MayHavePosted = true };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var refusal = await SocialPublishException.FromAsync(response, cancellationToken);
                throw (int)response.StatusCode >= 500 ? new SocialPublishException(refusal.Message) { MayHavePosted = true } : refusal;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException)
            {
                // It's posted either way; only its address is lost.
                return null;
            }
        }
    }

    /// <summary>The request never left: the address didn't resolve, or the connection (or its TLS) couldn't be made.</summary>
    private static bool NeverLeft(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError;
}
