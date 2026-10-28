using System.Net;
using CSAdvent.UnitTests.DeadLinks;
using CSAdvent.Web.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSAdvent.UnitTests.Snapshots;

public sealed class SnapshotSaverTests
{
    private const string Post = "https://www.jamietech.com/2017/12/01/open-source-software-a-year-in-review/";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACaptureIsAskedForWithTheAccountsKeysAndOnlyThePostsAddress()
    {
        string? form = null;
        var handler = new LinkProbeTests.FakeHandler(request =>
        {
            form = request.Content!.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"url": "x", "job_id": "spn2-0b1f2c3d4e5f"}""");
        });
        var saver = new WaybackSnapshotSaver(WaybackSnapshotSaver.Configure(new HttpClient(handler), "access123:secret456"), NullLogger<WaybackSnapshotSaver>.Instance);

        Assert.Equal(CaptureAnswer.Pending("spn2-0b1f2c3d4e5f"), await saver.StartAsync(Post, CancellationToken));

        var request = handler.Requests.Single();
        Assert.Equal((HttpMethod.Post, WaybackSnapshotSaver.StartEndpoint), (request.Method, request.RequestUri!.OriginalString));
        Assert.Equal("LOW access123:secret456", string.Join(",", request.Headers.GetValues("Authorization")));
        Assert.Equal($"url={Uri.EscapeDataString(Post)}&skip_first_archive=1", form);
    }

    [Fact]
    public async Task ACapturesStatusIsAskedForByItsJob()
    {
        var handler = new LinkProbeTests.FakeHandler(_ => Json(HttpStatusCode.OK, """{"status": "pending", "job_id": "spn2-0b1f2c3d4e5f"}"""));
        var saver = new WaybackSnapshotSaver(WaybackSnapshotSaver.Configure(new HttpClient(handler), "a:b"), NullLogger<WaybackSnapshotSaver>.Instance);

        var answer = await saver.StatusAsync(new CaptureJob("spn2-0b1f2c3d4e5f", Post, DateTimeOffset.UnixEpoch), CancellationToken);

        Assert.Equal(CaptureAnswer.Pending("spn2-0b1f2c3d4e5f"), answer);
        Assert.Equal("https://web.archive.org/save/status/spn2-0b1f2c3d4e5f", handler.Requests.Single().RequestUri!.OriginalString);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "The Wayback Machine refused our archive.org keys.")]
    [InlineData(HttpStatusCode.TooManyRequests, "The Wayback Machine answered 429.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The Wayback Machine answered 503.")]
    public async Task AnArchiveThatRefusesOrIsDownIsNoAnswer(HttpStatusCode status, string detail)
    {
        var saver = new WaybackSnapshotSaver(WaybackSnapshotSaver.Configure(new HttpClient(new LinkProbeTests.FakeHandler(_ => Json(status, "{}"))), "a:b"), NullLogger<WaybackSnapshotSaver>.Instance);

        Assert.Equal(CaptureAnswer.NoAnswer(detail), await saver.StartAsync(Post, CancellationToken));
    }

    [Fact]
    public async Task AnArchiveThatCantBeReachedIsNoAnswer()
    {
        var saver = new WaybackSnapshotSaver(
            WaybackSnapshotSaver.Configure(new HttpClient(new LinkProbeTests.FakeHandler(_ => throw new HttpRequestException("No such host is known."))), "a:b"),
            NullLogger<WaybackSnapshotSaver>.Instance);

        Assert.Equal(CaptureState.NoAnswer, (await saver.StartAsync(Post, CancellationToken)).State);
    }

    [Fact]
    public void ARefusedRequestFailsWithWhatTheArchiveSaid() =>
        Assert.Equal(
            CaptureAnswer.Failed("The Wayback Machine said: This URL is in the Save Page Now service block list. (error:blocked-url)"),
            WaybackSnapshotSaver.ParseStart("""{"status": "error", "status_ext": "error:blocked-url", "message": "This URL is in the Save Page Now service block list."}"""));

    [Theory]
    [InlineData("""{"message": "You have already reached the limit of active sessions."}""", "The Wayback Machine said: You have already reached the limit of active sessions.")]
    [InlineData("""{"url": "x"}""", "The Wayback Machine didn't start a capture.")]
    [InlineData("<html>Too many requests</html>", "The Wayback Machine's answer wasn't readable.")]
    public void ARequestThatStartsNothingIsNoAnswer(string json, string detail) =>
        Assert.Equal(CaptureAnswer.NoAnswer(detail), WaybackSnapshotSaver.ParseStart(json));

    [Fact]
    public void ASavedCaptureIsAtItsTimestampAndTheAddressAfterRedirects()
    {
        var answer = WaybackSnapshotSaver.ParseStatus("""
            {"status": "success", "job_id": "spn2-0b1f2c3d4e5f", "timestamp": "20261201143015", "duration_sec": 6.2,
              "original_url": "https://jamietech.com/2017/12/01/open-source-software-a-year-in-review/"}
            """);

        Assert.Equal(
            CaptureAnswer.Saved(
                "https://web.archive.org/web/20261201143015/https://jamietech.com/2017/12/01/open-source-software-a-year-in-review/",
                new DateTimeOffset(2026, 12, 1, 14, 30, 15, TimeSpan.Zero)),
            answer);
    }

    [Fact]
    public void AFailedCaptureSaysWhy() =>
        Assert.Equal(
            CaptureAnswer.Failed("The Wayback Machine said: Couldn't resolve host for http://gone.example/. (error:invalid-host-resolution)"),
            WaybackSnapshotSaver.ParseStatus("""
                {"status": "error", "status_ext": "error:invalid-host-resolution", "exception": "[Errno -2] Name or service not known",
                  "message": "Couldn't resolve host for http://gone.example/."}
                """));

    [Theory]
    [InlineData("""{"status": "success", "timestamp": "2026", "original_url": "https://a.example/"}""")]
    [InlineData("""{"status": "success", "timestamp": "20261201143015", "original_url": "javascript:alert(1)"}""")]
    [InlineData("""{"status": "success", "timestamp": "20261201143015"}""")]
    [InlineData("""{"status": "queued"}""")]
    [InlineData("not json")]
    public void AStatusItCantReadIsNoAnswer(string json) =>
        Assert.Equal(CaptureState.NoAnswer, WaybackSnapshotSaver.ParseStatus(json).State);

    [Fact]
    public void ALongErrorIsCutShort() =>
        Assert.Equal(500, WaybackSnapshotSaver.ParseStatus($$"""{"status": "error", "message": "{{new string('x', 1000)}}"}""").Detail!.Length);

    [Fact]
    public async Task TheStandInSavesPostsWhenTheyWereAskedForExceptTheOnesThatSaySo()
    {
        var saver = new SampleSnapshotSaver();
        var requested = new DateTimeOffset(2690, 12, 3, 14, 0, 0, TimeSpan.Zero);
        Task<CaptureAnswer> StatusAsync(string url) => saver.StatusAsync(new CaptureJob(SampleSnapshotSaver.JobId, url, requested), CancellationToken);

        Assert.Equal(CaptureAnswer.Pending(SampleSnapshotSaver.JobId), await saver.StartAsync("https://ada.links.csadvent.test/working", CancellationToken));
        Assert.Equal(CaptureState.NoAnswer, (await saver.StartAsync("https://ada.links.csadvent.test/busy", CancellationToken)).State);
        Assert.Equal(
            CaptureAnswer.Saved("https://web.archive.org/web/26901203140000/https://ada.links.csadvent.test/working", requested),
            await StatusAsync("https://ada.links.csadvent.test/working"));
        Assert.Equal(CaptureState.Pending, (await StatusAsync("https://ada.links.csadvent.test/slow")).State);
        Assert.Equal(CaptureState.Failed, (await StatusAsync("https://ada.links.csadvent.test/gone")).State);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json) };
}
