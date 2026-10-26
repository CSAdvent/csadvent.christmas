using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Posts to C# Advent's Bluesky account through its PDS (XRPC). Bluesky doesn't find links, mentions or hashtags in a post
/// by itself, or fetch link cards, so this marks up our own as facets (never anything in the author's words, #155) and builds
/// the card from the entry's share card. It signs in with the app password for each post (Bluesky allows 30 sign-ins every 5
/// minutes). Each announcement has a record key of its own (<see cref="RecordKey"/>), and every try looks for it before
/// posting, so trying again never posts twice (#154).
/// </summary>
public sealed partial class BlueskyPublisher(HttpClient http, string handle, BlueskyCredentials credentials, TimeProvider timeProvider) : ISocialPublisher
{
    private const string Collection = "app.bsky.feed.post";

    public SocialPlatform Platform => SocialPlatform.Bluesky;

    public bool RetriesAreSafe => true;

    public async Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken)
    {
        var session = await SignInAsync(cancellationToken);
        var text = publication.Announcement.Text;

        var facets = new JsonArray();
        foreach (var facet in Facets(text, publication.Announcement.Link, publication.Announcement.Mention))
        {
            var feature = facet.Kind switch
            {
                FacetKind.Link => new JsonObject { ["$type"] = "app.bsky.richtext.facet#link", ["uri"] = facet.Value },
                FacetKind.Tag => new JsonObject { ["$type"] = "app.bsky.richtext.facet#tag", ["tag"] = facet.Value },
                // A mention needs the account's DID. A handle that doesn't resolve stays plain text.
                _ => await ResolveAsync(facet.Value, cancellationToken) is { } did
                    ? new JsonObject { ["$type"] = "app.bsky.richtext.facet#mention", ["did"] = did }
                    : null,
            };
            if (feature is not null)
            {
                facets.Add(new JsonObject
                {
                    ["index"] = new JsonObject { ["byteStart"] = facet.ByteStart, ["byteEnd"] = facet.ByteEnd },
                    ["features"] = new JsonArray(feature),
                });
            }
        }

        var record = new JsonObject
        {
            ["$type"] = "app.bsky.feed.post",
            ["text"] = text,
            ["createdAt"] = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
            ["langs"] = new JsonArray("en"),
            ["facets"] = facets,
            ["embed"] = new JsonObject
            {
                ["$type"] = "app.bsky.embed.external",
                ["external"] = new JsonObject
                {
                    ["uri"] = publication.Announcement.Link,
                    ["title"] = publication.Title,
                    ["description"] = publication.Description,
                    ["thumb"] = await UploadAsync(session, publication.Card, cancellationToken),
                },
            },
        };

        // An earlier try that never heard back may have made it, under the same key.
        var key = RecordKey(publication.IdempotencyKey);
        if (await HasPostAsync(session, key, cancellationToken))
        {
            return PostUrl(key);
        }

        // With the key, a second post can't be made: the PDS refuses a key that's taken.
        using var request = Request(HttpMethod.Post, "com.atproto.repo.createRecord", session);
        request.Content = JsonContent.Create(new JsonObject
        {
            ["repo"] = session.Did,
            ["collection"] = Collection,
            ["rkey"] = key,
            ["record"] = record,
        });

        // at://{did}/app.bsky.feed.post/{rkey} is https://bsky.app/profile/{handle}/post/{rkey} on the web.
        var uri = (await http.SendPostAsync<Created>(request, Platform, cancellationToken))?.Uri;
        return PostUrl(uri?.Split('/') is { Length: > 0 } parts && parts[^1] is { Length: > 0 } rkey ? rkey : key);
    }

    public async Task CheckAsync(CancellationToken cancellationToken) => await SignInAsync(cancellationToken);

    /// <summary>
    /// The post's own link, the author's handle and our hashtags, with their UTF-8 byte ranges, which is how Bluesky counts.
    /// Nothing else becomes a facet, so a link, handle or hashtag in a post's title or summary stays plain text (#155).
    /// </summary>
    /// <param name="link">The post's link: the only one made live.</param>
    /// <param name="mention">The author's handle, if the post mentions them: the only one made a mention.</param>
    public static IReadOnlyList<Facet> Facets(string text, string link, string? mention) =>
        [.. Facets(text).Where(facet => facet.Kind switch
        {
            FacetKind.Link => facet.Value == link,
            FacetKind.Mention => mention is not null && string.Equals(facet.Value, mention, StringComparison.OrdinalIgnoreCase),
            _ => AnnouncementFormatter.OurHashtags.Contains(facet.Value),
        })];

    /// <summary>
    /// Every link, mention and hashtag in a post, with its UTF-8 byte range. Hashtags and mentions inside a link are part of
    /// the link. "C#" isn't a hashtag, because it follows a letter.
    /// </summary>
    public static IReadOnlyList<Facet> Facets(string text)
    {
        var links = Link().Matches(text).Select(m => (m.Index, m.Length, Kind: FacetKind.Link, Value: m.Value)).ToList();
        var others = Mention().Matches(text).Select(m => (m.Index, m.Length, Kind: FacetKind.Mention, Value: m.Groups["handle"].Value))
            .Concat(Hashtag().Matches(text).Select(m => (m.Index, m.Length, Kind: FacetKind.Tag, Value: m.Groups["tag"].Value)))
            .Where(f => !links.Any(link => f.Index < link.Index + link.Length && link.Index < f.Index + f.Length));

        return [.. links.Concat(others)
            .OrderBy(f => f.Index)
            .Select(f => new Facet(
                Encoding.UTF8.GetByteCount(text.AsSpan(0, f.Index)),
                Encoding.UTF8.GetByteCount(text.AsSpan(0, f.Index + f.Length)),
                f.Kind,
                f.Value))];
    }

    private async Task<Session> SignInAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Xrpc("com.atproto.server.createSession"))
        {
            Content = JsonContent.Create(new { identifier = handle, password = credentials.AppPassword }),
        };
        using var response = await http.SendAsync(request, cancellationToken);
        await SocialPublishException.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<Session>(cancellationToken)
            ?? throw new SocialPublishException("Bluesky didn't return a session.");
    }

    private async Task<string?> ResolveAsync(string mentioned, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(Xrpc($"com.atproto.identity.resolveHandle?handle={Uri.EscapeDataString(mentioned)}"), cancellationToken);
        return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<Resolved>(cancellationToken))?.Did : null;
    }

    private async Task<JsonNode?> UploadAsync(Session session, byte[] image, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "com.atproto.repo.uploadBlob", session);
        request.Content = new ByteArrayContent(image) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } };
        using var response = await http.SendAsync(request, cancellationToken);
        await SocialPublishException.EnsureSuccessAsync(response, cancellationToken);

        // The blob goes into the post exactly as the PDS described it.
        return (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))?["blob"]?.DeepClone();
    }

    /// <summary>
    /// The record key for an announcement's post: the same for every try at it, made from its idempotency key rather than
    /// the clock. Bluesky posts' keys are TIDs (13 base32-sortable characters: 53 bits of microseconds since 1970, then a
    /// 10-bit clock identifier). This one's time is a fixed moment in the past plus 40 bits of the key's hash (under 13 days),
    /// and its clock identifier is 10 more bits of the hash.
    /// </summary>
    public static string RecordKey(string idempotencyKey)
    {
        var hash = BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        var microseconds = KeyEpoch.ToUnixTimeMilliseconds() * 1000 + (long)(hash & ((1UL << 40) - 1));
        var tid = (microseconds << 10) | (long)((hash >> 40) & 0x3FF);

        Span<char> key = stackalloc char[13];
        for (var i = key.Length - 1; i >= 0; i--, tid >>= 5)
        {
            key[i] = TidCharacters[(int)(tid & 31)];
        }

        return new string(key);
    }

    private static readonly DateTimeOffset KeyEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string TidCharacters = "234567abcdefghijklmnopqrstuvwxyz";

    /// <summary>Whether the account has a post under this key. A missing one is a 400 whose error is RecordNotFound.</summary>
    private async Task<bool> HasPostAsync(Session session, string key, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"com.atproto.repo.getRecord?repo={Uri.EscapeDataString(session.Did)}&collection={Collection}&rkey={key}", session);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode is HttpStatusCode.BadRequest && IsRecordNotFound(await response.Content.ReadAsStringAsync(cancellationToken)))
        {
            return false;
        }

        throw await SocialPublishException.FromAsync(response, cancellationToken);
    }

    private static bool IsRecordNotFound(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind is JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error) && error.ValueKind is JsonValueKind.String && error.GetString() == "RecordNotFound";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string PostUrl(string key) => $"https://bsky.app/profile/{handle}/post/{key}";

    private HttpRequestMessage Request(HttpMethod method, string nsid, Session session) =>
        new(method, Xrpc(nsid)) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", session.AccessJwt) } };

    private string Xrpc(string nsidAndQuery) => $"{credentials.Service.TrimEnd('/')}/xrpc/{nsidAndQuery}";

    [GeneratedRegex(@"https?://[^\s]+")]
    private static partial Regex Link();

    // "@alice.bsky.social", including in "(@alice.bsky.social)".
    [GeneratedRegex(@"(?<![\w@])@(?<handle>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Mention();

    [GeneratedRegex(@"(?<![\w#&])#(?<tag>[\p{L}][\p{L}\p{N}_]{0,63})")]
    private static partial Regex Hashtag();

    private sealed record Session(string AccessJwt, string Did);

    private sealed record Resolved(string? Did);

    private sealed record Created(string? Uri);
}

public enum FacetKind
{
    Link,
    Mention,
    Tag,
}

/// <summary>A link, mention or hashtag in a Bluesky post, by UTF-8 byte range. Its value is the address, handle or tag.</summary>
public sealed record Facet(int ByteStart, int ByteEnd, FacetKind Kind, string Value);
