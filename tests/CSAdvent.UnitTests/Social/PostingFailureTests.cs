using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Social;

/// <summary>What a failed post means (#154), and what the organizers are told when posting stops on one (#161).</summary>
public sealed partial class PostingFailureTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static SocialPublication Publication(SocialPlatform platform) => new(
        2026,
        6,
        2,
        "Span<T> all the way down",
        "By Ada, for C# Advent 2026: December 6, post 2.",
        new FormattedAnnouncement(platform, "C# Advent 2026, December 6, part 2", "https://csadvent.christmas/2026/day/6/2", 40, 300, null, null),
        [1, 2, 3]);

    private static FacebookPublisher Facebook(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakePlatform(respond)), new FacebookCredentials { PageId = "12345", PageToken = "page-token" });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ATimeoutOnceThePostIsSentMayHavePostedIt()
    {
        var failure = await Assert.ThrowsAsync<SocialPublishException>(() =>
            Facebook(_ => throw new TaskCanceledException("The request was canceled.", new TimeoutException())).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken));

        Assert.True(failure.MayHavePosted);
        Assert.Equal("Facebook didn't answer in time.", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task AServerErrorMayHavePostedIt(HttpStatusCode status)
    {
        var failure = await Assert.ThrowsAsync<SocialPublishException>(() =>
            Facebook(_ => Json(status, """{ "error": { "message": "An unknown error has occurred.", "code": 1 } }""")).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken));

        Assert.True(failure.MayHavePosted);
        Assert.EndsWith("An unknown error has occurred.", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ARefusalDidNot(HttpStatusCode status)
    {
        var failure = await Assert.ThrowsAsync<SocialPublishException>(() =>
            Facebook(_ => Json(status, """{ "error": { "message": "Refused." } }""")).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken));

        Assert.False(failure.MayHavePosted);
    }

    [Fact]
    public async Task ADroppedConnectionMayHavePostedIt()
    {
        var failure = await Assert.ThrowsAsync<SocialPublishException>(() =>
            Facebook(_ => throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely.")).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken));

        Assert.True(failure.MayHavePosted);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task ARequestThatNeverLeftDidNot(HttpRequestError error)
    {
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Facebook(_ => throw new HttpRequestException(error, "Couldn't connect.")).PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken));

        Assert.Equal(error, failure.HttpRequestError);
    }

    [Fact]
    public async Task ASuccessWhoseAnswerCantBeReadIsStillPosted()
    {
        var url = await Facebook(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>OK</html>") })
            .PublishAsync(Publication(SocialPlatform.Facebook), CancellationToken);

        Assert.Null(url);
    }

    [Fact]
    public async Task ATryReturnsWhatWentWrongRatherThanThrowing()
    {
        var attempt = await SocialPostAttempts.TryAsync(
            Facebook(_ => throw new TaskCanceledException("The request was canceled.", new TimeoutException())), Publication(SocialPlatform.Facebook), CancellationToken);

        Assert.False(attempt.Posted);
        Assert.True(attempt.MayHavePosted);
        Assert.Equal("Facebook didn't answer in time.", attempt.Error);
        Assert.IsType<SocialPublishException>(attempt.Exception);
    }

    [Fact]
    public async Task TheAppStoppingIsntAFailure()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SocialPostAttempts.TryAsync(Facebook(_ => Json(HttpStatusCode.OK, """{ "id": "1_2" }""")), Publication(SocialPlatform.Facebook), stopping.Token));
    }

    [Theory]
    [InlineData(false, false, SocialPostStatus.Failed)]
    [InlineData(false, true, SocialPostStatus.Failed)]
    [InlineData(true, false, SocialPostStatus.Unconfirmed)]
    [InlineData(true, true, SocialPostStatus.Failed)]
    public void APostThatMayHaveGoneOutWaitsForAnOrganizerUnlessRetriesAreSafe(bool mayHavePosted, bool retriesAreSafe, SocialPostStatus status) =>
        Assert.Equal(status, SocialPostAttempts.StatusAfter(new PostAttempt(null, "It failed.", mayHavePosted), retriesAreSafe));

    [Fact]
    public void APostedOneIsPosted() =>
        Assert.Equal(SocialPostStatus.Posted, SocialPostAttempts.StatusAfter(new PostAttempt("https://www.facebook.com/1_2", null), retriesAreSafe: false));

    [Fact]
    public void OnlyBlueskysRetriesAreSafe()
    {
        var http = new HttpClient();

        Assert.True(((ISocialPublisher)new BlueskyPublisher(http, "csadvent.christmas", new BlueskyCredentials(), TimeProvider.System)).RetriesAreSafe);
        Assert.False(((ISocialPublisher)new MastodonPublisher(http, "mastodon.social", "token")).RetriesAreSafe);
        Assert.False(((ISocialPublisher)new FacebookPublisher(http, new FacebookCredentials())).RetriesAreSafe);
        Assert.False(((ISocialPublisher)new XPublisher(http, "csadvent", new XCredentials(), TimeProvider.System)).RetriesAreSafe);
    }

    [Fact]
    public void EachBlueskyPostHasARecordKeyOfItsOwnThatsAValidTid()
    {
        var key = BlueskyPublisher.RecordKey("csadvent-2026-6-2-bluesky");

        // The TID syntax, from the AT Protocol's spec.
        Assert.Matches(Tid(), key);
        Assert.Equal(key, BlueskyPublisher.RecordKey("csadvent-2026-6-2-bluesky"));
        Assert.NotEqual(key, BlueskyPublisher.RecordKey("csadvent-2026-6-1-bluesky"));
        Assert.NotEqual(key, BlueskyPublisher.RecordKey("csadvent-2026-signupsopen--bluesky"));

        // Its time is in the past, not in the future: early January 2026.
        var time = DateTimeOffset.UnixEpoch.AddTicks((Decode(key) >> 10) * 10);
        Assert.InRange(time, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task BlueskyPostsUnderItsRecordKey()
    {
        var bluesky = new FakePlatform(Bluesky(found: false));

        var url = await Bluesky(bluesky).PublishAsync(Publication(SocialPlatform.Bluesky), CancellationToken);

        var key = BlueskyPublisher.RecordKey("csadvent-2026-6-2-bluesky");
        Assert.Equal($"https://bsky.app/profile/csadvent.christmas/post/{key}", url);
        Assert.Contains(bluesky.Requests, r => r.Url == $"https://bsky.social/xrpc/com.atproto.repo.getRecord?repo=did%3Aplc%3Acsadvent&collection=app.bsky.feed.post&rkey={key}");
        var create = bluesky.Requests.Single(r => r.Url.EndsWith("createRecord", StringComparison.Ordinal));
        Assert.Equal(key, (string?)JsonNode.Parse(create.Body)!["rkey"]);
    }

    [Fact]
    public async Task BlueskyFindsThePostAnEarlierTryMadeRatherThanPostingAgain()
    {
        var bluesky = new FakePlatform(Bluesky(found: true));

        var url = await Bluesky(bluesky).PublishAsync(Publication(SocialPlatform.Bluesky), CancellationToken);

        Assert.Equal($"https://bsky.app/profile/csadvent.christmas/post/{BlueskyPublisher.RecordKey("csadvent-2026-6-2-bluesky")}", url);
        Assert.DoesNotContain(bluesky.Requests, r => r.Url.EndsWith("createRecord", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BlueskyDoesntPostWhenItCantTellWhetherThePostIsThere()
    {
        var bluesky = new FakePlatform(request => request.RequestUri!.AbsolutePath.EndsWith("getRecord", StringComparison.Ordinal)
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Bluesky(found: false)(request));

        var failure = await Assert.ThrowsAsync<SocialPublishException>(() => Bluesky(bluesky).PublishAsync(Publication(SocialPlatform.Bluesky), CancellationToken));

        Assert.False(failure.MayHavePosted);
        Assert.DoesNotContain(bluesky.Requests, r => r.Url.EndsWith("createRecord", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOrganizersAreAskedToCheckAPostThatMayHaveGoneOut()
    {
        var email = SocialPostingAlerts.Stopped(
            StoppedPost.Entry(SocialPlatform.Facebook, 2026, 6, 2, 2, "Span<T> all the way down", mayHavePosted: true, "Facebook didn't answer in time.", 1), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("social-post-unconfirmed", email.Kind);
        Assert.Equal("Did C# Advent 2026, December 6, post 2 go out on Facebook?", email.Subject);
        Assert.Contains("C# Advent 2026, December 6, post 2 (“Span<T> all the way down”)", text);
        Assert.Contains("It won't be tried again by itself, because that could post it twice.", text);
        Assert.Contains("If it's there, press Mark as posted", text);
        Assert.Equal(new DetailRow("What happened", "Facebook didn't answer in time."), Assert.Single(Assert.Single(email.Body.OfType<DetailsBlock>()).Rows));
        Assert.Equal("https://csadvent.christmas/manage/social?year=2026&day=6", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.False(email.Transactional);
    }

    [Fact]
    public void TheOrganizersAreToldWhenAPostGivesUp()
    {
        var email = SocialPostingAlerts.Stopped(
            StoppedPost.Entry(SocialPlatform.X, 2026, 6, 2, 2, "Span<T>", mayHavePosted: false, "402 Payment Required:\nNo credits.", 3), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("social-post-gave-up", email.Kind);
        Assert.Equal("X didn't post C# Advent 2026, December 6, post 2", email.Subject);
        Assert.Contains("C# Advent tried 3 times to post", text);
        Assert.Contains("Then press Retry now", text);
        Assert.Equal(new DetailRow("What happened", "402 Payment Required:No credits."), Assert.Single(Assert.Single(email.Body.OfType<DetailsBlock>()).Rows));
    }

    [Theory]
    [InlineData(SeasonPostKind.SignUpsOpen, "Did the C# Advent 2026 “sign-ups are open” post go out on Mastodon?", "https://csadvent.christmas/manage/social/signups?year=2026")]
    [InlineData(SeasonPostKind.Retrospective, "Did the C# Advent 2026 retrospective post go out on Mastodon?", "https://csadvent.christmas/manage/retrospectives/2026")]
    public void SeasonPostsLinkToTheirOwnPage(SeasonPostKind kind, string subject, string page)
    {
        var email = SocialPostingAlerts.Stopped(StoppedPost.Season(SocialPlatform.Mastodon, 2026, kind, mayHavePosted: true, SocialPostAttempts.Interrupted), Site);

        Assert.Equal(subject, email.Subject);
        Assert.Equal(page, Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.Contains("If it isn't, press Post now.", EmailText.Render(email, Site.BaseUrl));
    }

    [Fact]
    public void AOnePostADaySeasonHasNoPostNumber() =>
        Assert.Equal("C# Advent 2017, December 3", StoppedPost.Entry(SocialPlatform.X, 2017, 3, 1, 1, null, false, "Refused.", 3).Name);

    private static BlueskyPublisher Bluesky(FakePlatform bluesky) =>
        new(new HttpClient(bluesky), "csadvent.christmas", new BlueskyCredentials { AppPassword = "app-password" }, new FakeTimeProvider(new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero)));

    private static Func<HttpRequestMessage, HttpResponseMessage> Bluesky(bool found) => request => request.RequestUri!.AbsolutePath switch
    {
        "/xrpc/com.atproto.server.createSession" => Json(HttpStatusCode.OK, """{ "accessJwt": "jwt", "did": "did:plc:csadvent" }"""),
        "/xrpc/com.atproto.repo.getRecord" => found
            ? Json(HttpStatusCode.OK, """{ "uri": "at://did:plc:csadvent/app.bsky.feed.post/x", "cid": "bafy", "value": {} }""")
            : Json(HttpStatusCode.BadRequest, """{ "error": "RecordNotFound", "message": "Could not locate record" }"""),
        "/xrpc/com.atproto.repo.uploadBlob" => Json(HttpStatusCode.OK, """{ "blob": { "$type": "blob", "ref": { "$link": "bafkcard" }, "mimeType": "image/jpeg", "size": 3 } }"""),
        "/xrpc/com.atproto.repo.createRecord" => Json(HttpStatusCode.OK, "{}"),
        _ => Json(HttpStatusCode.NotFound, "{}"),
    };

    private static long Decode(string tid) => tid.Aggregate(0L, (value, c) => (value << 5) | (long)"234567abcdefghijklmnopqrstuvwxyz".IndexOf(c, StringComparison.Ordinal));

    [GeneratedRegex("^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$")]
    private static partial Regex Tid();

    private sealed class FakePlatform(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
