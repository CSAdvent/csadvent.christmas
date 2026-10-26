using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Social;

public sealed class PublisherTests
{
    private const string Text = "C# Advent 2026, December 6, part 2: “Span<T> all the way down 🚀” by Ada (@ada.bsky.social)\n\nhttps://csadvent.christmas/2026/day/6/2?via=bluesky\n\n#csharp #dotnet #csadvent";
    private const string Link = "https://csadvent.christmas/2026/day/6/2?via=bluesky";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static SocialPublication Publication(SocialPlatform platform) => new(
        2026,
        6,
        2,
        "Span<T> all the way down 🚀",
        "By Ada, for C# Advent 2026: December 6, post 2.",
        new FormattedAnnouncement(platform, Text, Link, 200, 300, null, null) { Mention = "ada.bsky.social" },
        [1, 2, 3]);

    [Fact]
    public async Task MastodonPostsPubliclyWithAnIdempotencyKey()
    {
        var mastodon = new FakePlatform(_ => (HttpStatusCode.OK, """{ "id": "1", "url": "https://mastodon.social/@csadvent/1" }"""));

        var url = await new MastodonPublisher(new HttpClient(mastodon), "mastodon.social", "token").PublishAsync(Publication(SocialPlatform.Mastodon), CancellationToken);

        Assert.Equal("https://mastodon.social/@csadvent/1", url);
        var request = Assert.Single(mastodon.Requests);
        Assert.Equal((HttpMethod.Post, "https://mastodon.social/api/v1/statuses", "Bearer token"), (request.Method, request.Url, request.Authorization));
        Assert.Equal("csadvent-2026-6-2-mastodon", request.Headers["Idempotency-Key"]);
        Assert.Contains("visibility=public", request.Body);
        Assert.Contains($"status={Uri.EscapeDataString(Text).Replace("%20", "+")}", request.Body);
    }

    [Fact]
    public async Task AFailedPostSaysWhatThePlatformSaid()
    {
        var mastodon = new FakePlatform(_ => (HttpStatusCode.Unauthorized, """{ "error": "The access token is invalid" }"""));

        var failure = await Assert.ThrowsAsync<SocialPublishException>(() =>
            new MastodonPublisher(new HttpClient(mastodon), "mastodon.social", "revoked").PublishAsync(Publication(SocialPlatform.Mastodon), CancellationToken));

        Assert.Equal("401 Unauthorized: The access token is invalid", failure.Message);
    }

    [Fact]
    public async Task FacebookPostsToThePageFeedAsThePage()
    {
        var facebook = new FakePlatform(_ => (HttpStatusCode.OK, """{ "id": "12345_678" }"""));
        var credentials = new FacebookCredentials { PageId = "12345", PageToken = "page-token" };

        var url = await new FacebookPublisher(new HttpClient(facebook), credentials).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken);

        Assert.Equal("https://www.facebook.com/12345_678", url);
        var request = Assert.Single(facebook.Requests);
        Assert.Equal((HttpMethod.Post, "https://graph.facebook.com/v24.0/12345/feed", "Bearer page-token"), (request.Method, request.Url, request.Authorization));
        Assert.Contains($"link={Uri.EscapeDataString(Link)}", request.Body);
        Assert.DoesNotContain("page-token", request.Url);
    }

    [Fact]
    public async Task BlueskyPostsWithFacetsAndALinkCard()
    {
        var bluesky = new FakePlatform(request => request.RequestUri!.AbsolutePath switch
        {
            "/xrpc/com.atproto.server.createSession" => (HttpStatusCode.OK, """{ "accessJwt": "jwt", "did": "did:plc:csadvent" }"""),
            "/xrpc/com.atproto.identity.resolveHandle" => (HttpStatusCode.OK, """{ "did": "did:plc:ada" }"""),
            "/xrpc/com.atproto.repo.getRecord" => (HttpStatusCode.BadRequest, """{ "error": "RecordNotFound", "message": "Could not locate record" }"""),
            "/xrpc/com.atproto.repo.uploadBlob" => (HttpStatusCode.OK, """{ "blob": { "$type": "blob", "ref": { "$link": "bafkcard" }, "mimeType": "image/jpeg", "size": 3 } }"""),
            "/xrpc/com.atproto.repo.createRecord" => (HttpStatusCode.OK, """{ "uri": "at://did:plc:csadvent/app.bsky.feed.post/3kabc", "cid": "bafyrecord" }"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero));

        var url = await new BlueskyPublisher(new HttpClient(bluesky), "csadvent.christmas", new BlueskyCredentials { AppPassword = "app-password" }, clock)
            .PublishAsync(Publication(SocialPlatform.Bluesky), CancellationToken);

        Assert.Equal("https://bsky.app/profile/csadvent.christmas/post/3kabc", url);
        Assert.Contains("\"identifier\":\"csadvent.christmas\"", bluesky.Requests[0].Body);
        Assert.Equal("?handle=ada.bsky.social", new Uri(bluesky.Requests.Single(r => r.Url.Contains("resolveHandle")).Url).Query);

        var create = bluesky.Requests.Single(r => r.Url.EndsWith("createRecord", StringComparison.Ordinal));
        Assert.Equal("Bearer jwt", create.Authorization);
        var record = JsonNode.Parse(create.Body)!["record"]!;
        Assert.Equal(Text, (string?)record["text"]);
        Assert.Equal("2026-12-06T15:00:00.000Z", (string?)record["createdAt"]);
        Assert.Equal(
            ["app.bsky.richtext.facet#mention", "app.bsky.richtext.facet#link", "app.bsky.richtext.facet#tag", "app.bsky.richtext.facet#tag", "app.bsky.richtext.facet#tag"],
            record["facets"]!.AsArray().Select(f => (string?)f!["features"]![0]!["$type"]));
        Assert.Equal("did:plc:ada", (string?)record["facets"]![0]!["features"]![0]!["did"]);

        var card = record["embed"]!["external"]!;
        Assert.Equal((Link, "Span<T> all the way down 🚀", "bafkcard"), ((string?)card["uri"], (string?)card["title"], (string?)card["thumb"]!["ref"]!["$link"]));
    }

    [Fact]
    public async Task XPostsAsTheAccountWithAnOAuthSignature()
    {
        var x = new FakePlatform(_ => (HttpStatusCode.Created, """{ "data": { "id": "1850000000000000001", "text": "…" } }"""));
        var credentials = new XCredentials { ApiKey = "api-key", ApiKeySecret = "api-secret", AccessToken = "access-token", AccessTokenSecret = "access-secret" };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero));

        var url = await new XPublisher(new HttpClient(x), "csadvent", credentials, clock).PublishAsync(Publication(SocialPlatform.X), CancellationToken);

        Assert.Equal("https://x.com/csadvent/status/1850000000000000001", url);
        var request = Assert.Single(x.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.x.com/2/tweets"), (request.Method, request.Url));
        Assert.Equal(Text, (string?)JsonNode.Parse(request.Body)!["text"]);
        Assert.StartsWith("OAuth oauth_consumer_key=\"api-key\", oauth_nonce=\"", request.Authorization);
        Assert.Contains("oauth_timestamp=\"1796569200\", oauth_token=\"access-token\", oauth_version=\"1.0\"", request.Authorization);
        Assert.DoesNotContain("secret", request.Authorization);
    }

    [Theory]
    // X's own worked example, from its OAuth 1.0a docs ("Creating a signature"), before and after it moved to api.x.com.
    [InlineData("https://api.twitter.com/1.1/statuses/update.json?include_entities=true", "hCtSmYh%2BiHYCEqBWrE7C7hYmtUk%3D")]
    [InlineData("https://api.x.com/1.1/statuses/update.json?include_entities=true", "Ls93hJiZbQ3akF3HF3x1Bz8%2FzU4%3D")]
    public void XRequestsAreSignedTheOAuthWay(string url, string signature)
    {
        var credentials = new XCredentials
        {
            ApiKey = "xvz1evFS4wEEPTGEFPHBog",
            ApiKeySecret = "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw",
            AccessToken = "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb",
            AccessTokenSecret = "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE",
        };

        var authorization = XPublisher.Authorization(
            HttpMethod.Post,
            new Uri(url),
            [KeyValuePair.Create("status", "Hello Ladies + Gentlemen, a signed OAuth request!")],
            credentials,
            DateTimeOffset.FromUnixTimeSeconds(1318622958),
            "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg");

        Assert.Contains($"oauth_signature=\"{signature}\"", authorization);
    }

    [Fact]
    public void FacetsAreCountedInUtf8Bytes()
    {
        var bytes = Encoding.UTF8.GetBytes(Text);

        var facets = BlueskyPublisher.Facets(Text);

        Assert.Equal(
            ["@ada.bsky.social", Link, "#csharp", "#dotnet", "#csadvent"],
            facets.Select(f => Encoding.UTF8.GetString(bytes[f.ByteStart..f.ByteEnd])));
        Assert.Equal([FacetKind.Mention, FacetKind.Link, FacetKind.Tag, FacetKind.Tag, FacetKind.Tag], facets.Select(f => f.Kind));
        Assert.Equal(["ada.bsky.social", Link, "csharp", "dotnet", "csadvent"], facets.Select(f => f.Value));
    }

    [Fact]
    public void OnlyOurLinkTheAuthorsHandleAndOurHashtagsBecomeFacets()
    {
        // A title with a link, a handle and a hashtag of its own, as it would be if it weren't made inert first.
        const string text = "C# Advent 2026, December 6, part 2: “Free licenses https://scam.example @someone.bsky.social #giveaway” by Ada (@ada.bsky.social)\n\n" +
            "https://csadvent.christmas/2026/day/6/2?via=bluesky\n\n#csharp #dotnet #csadvent";
        var bytes = Encoding.UTF8.GetBytes(text);

        var facets = BlueskyPublisher.Facets(text, Link, "ada.bsky.social");

        Assert.Equal(
            ["@ada.bsky.social", Link, "#csharp", "#dotnet", "#csadvent"],
            facets.Select(f => Encoding.UTF8.GetString(bytes[f.ByteStart..f.ByteEnd])));
        Assert.DoesNotContain(BlueskyPublisher.Facets(text, Link, mention: null), f => f.Kind is FacetKind.Mention);
    }

    [Fact]
    public void CSharpIsNotAHashtag() =>
        Assert.Empty(BlueskyPublisher.Facets("What's new in C# 14, and F#"));

    [Theory]
    [InlineData("""{ "error": "The access token is invalid" }""", "The access token is invalid")]
    [InlineData("""{ "error": "AuthenticationRequired", "message": "Invalid identifier or password" }""", "Invalid identifier or password")]
    [InlineData("""{ "error": { "message": "Invalid OAuth access token.", "type": "OAuthException", "code": 190 } }""", "Invalid OAuth access token.")]
    [InlineData("""{ "title": "Forbidden", "detail": "You are not allowed to create a Tweet with duplicate content.", "type": "about:blank", "status": 403 }""", "You are not allowed to create a Tweet with duplicate content.")]
    [InlineData("""{ "errors": [{ "message": "Could not authenticate you", "code": 32 }] }""", "Could not authenticate you")]
    [InlineData("<html>Bad gateway</html>", null)]
    public void ReadsEachPlatformsErrors(string body, string? message) =>
        Assert.Equal(message, SocialPublishException.ErrorMessage(body));

    [Fact]
    public void OnlyPlatformsWithCredentialsPostForReal()
    {
        var publishers = Publishers(new SocialPostingOptions
        {
            Mastodon = { AccessToken = "token" },
            X = { ApiKey = "key", ApiKeySecret = "secret", AccessToken = "token", AccessTokenSecret = "token-secret" },
        });

        Assert.IsType<MastodonPublisher>(publishers.For(SocialPlatform.Mastodon));
        Assert.IsType<XPublisher>(publishers.For(SocialPlatform.X));
        Assert.Null(publishers.For(SocialPlatform.Bluesky));
        Assert.Null(publishers.For(SocialPlatform.LinkedIn));
    }

    [Fact]
    public void XNeedsAllFourOfItsKeys()
    {
        var publishers = Publishers(new SocialPostingOptions { X = { ApiKey = "key", ApiKeySecret = "secret", AccessToken = "token" } });

        Assert.Null(publishers.For(SocialPlatform.X));
    }

    [Fact]
    public void DryRunStandsInForMissingCredentialsButNeverForLinkedInOrReddit()
    {
        var publishers = Publishers(new SocialPostingOptions { DryRun = true, Facebook = { PageId = "1", PageToken = "token" } });

        Assert.IsType<FacebookPublisher>(publishers.For(SocialPlatform.Facebook));
        Assert.IsType<DryRunPublisher>(publishers.For(SocialPlatform.Bluesky));
        Assert.IsType<DryRunPublisher>(publishers.For(SocialPlatform.X));
        Assert.Null(publishers.For(SocialPlatform.Reddit));
    }

    [Fact]
    public void EachPartHasItsOwnTime()
    {
        var options = new SocialPostingOptions { PostAt = [new(10, 0), new(14, 0)] };

        Assert.Equal([new TimeOnly(10, 0), new TimeOnly(14, 0), new TimeOnly(14, 0)], new[] { 1, 2, 3 }.Select(options.TimeFor));
    }

    [Fact]
    public void FailedPostsWaitLongerEachTime() =>
        Assert.Equal([5, 10, 20], new[] { 1, 2, 3 }.Select(attempts => SocialPostingService.WaitAfter(attempts).TotalMinutes));

    private static SocialPublishers Publishers(SocialPostingOptions options) =>
        new(Options.Create(options), Options.Create(new SocialAccountsOptions { Bluesky = "csadvent.christmas", Mastodon = "@csadvent@mastodon.social" }), new HttpClient(), TimeProvider.System, NullLogger<DryRunPublisher>.Instance);

    private sealed class FakePlatform(Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
            var (status, json) = respond(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Url, string? Authorization, string Body, Dictionary<string, string> Headers);
}
