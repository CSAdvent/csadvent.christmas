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

    /// <summary>Posts it. Returns the post's address, if the platform gives one. Throws <see cref="SocialPublishException"/> if the platform refuses.</summary>
    Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken);

    /// <summary>Signs in, or makes the cheapest authenticated call, so a revoked or expired credential shows up before it's needed.</summary>
    Task CheckAsync(CancellationToken cancellationToken);
}

/// <summary>A platform refused a request. The message is the platform's own, for the organizers.</summary>
public sealed class SocialPublishException(string message) : Exception(message)
{
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
