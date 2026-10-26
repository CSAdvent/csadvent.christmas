using System.Net;
using CSAdvent.Web.DeadLinks;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class ArchiveLookupTests
{
    private const string Post = "https://www.jamietech.com/2017/12/01/open-source-software-a-year-in-review/";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void AsksForTheSnapshotClosestToTheReveal() =>
        Assert.Equal(
            "https://archive.org/wayback/available?url=https%3A%2F%2Fwww.jamietech.com%2F2017%2F12%2F01%2Fopen-source-software-a-year-in-review%2F&timestamp=20171202",
            WaybackArchiveLookup.RequestUrl(Post, new DateOnly(2017, 12, 2)));

    [Fact]
    public void ASavedPageThatLoadedIsTheCopyOverHttps()
    {
        var answer = WaybackArchiveLookup.Parse("""
            {"url": "POST", "archived_snapshots": {"closest": {"status": "200", "available": true,
              "url": "http://web.archive.org/web/20171205101112/POST", "timestamp": "20171205101112"}}}
            """.Replace("POST", Post, StringComparison.Ordinal));

        Assert.Equal(ArchiveAnswer.Copy($"https://web.archive.org/web/20171205101112/{Post}"), answer);
    }

    [Theory]
    [InlineData("""{"url": "x", "archived_snapshots": {}}""")]
    [InlineData("""{"url": "x", "archived_snapshots": {"closest": {"status": "404", "available": true, "url": "http://web.archive.org/web/2017/x"}}}""")]
    [InlineData("""{"url": "x", "archived_snapshots": {"closest": {"status": "200", "available": false, "url": "http://web.archive.org/web/2017/x"}}}""")]
    [InlineData("""{"url": "x", "archived_snapshots": {"closest": {"status": "200", "available": true, "url": "https://evil.example/web/2017/x"}}}""")]
    public void AnythingElseIsNoCopy(string json) =>
        Assert.Equal(ArchiveAnswer.NoCopy, WaybackArchiveLookup.Parse(json));

    [Fact]
    public void AnAnswerThatIsntJsonIsNoAnswer() =>
        Assert.Equal(ArchiveAnswer.NoAnswer, WaybackArchiveLookup.Parse("<html>Too many requests</html>"));

    [Fact]
    public async Task AnArchiveThatIsDownIsAskedAgainLater()
    {
        var handler = new LinkProbeTests.FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var lookup = new WaybackArchiveLookup(new HttpClient(handler), NullLogger<WaybackArchiveLookup>.Instance);

        Assert.Equal(ArchiveAnswer.NoAnswer, await lookup.FindAsync(Post, new DateOnly(2017, 12, 2), CancellationToken));
        Assert.Equal(WaybackArchiveLookup.RequestUrl(Post, new DateOnly(2017, 12, 2)), handler.Requests.Single().RequestUri!.OriginalString);
    }

    [Fact]
    public async Task TheLookupReadsTheArchivesAnswer()
    {
        var json = """{"archived_snapshots": {"closest": {"status": "200", "available": true, "url": "http://web.archive.org/web/20171205101112/POST"}}}"""
            .Replace("POST", Post, StringComparison.Ordinal);
        var lookup = new WaybackArchiveLookup(
            new HttpClient(new LinkProbeTests.FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })),
            NullLogger<WaybackArchiveLookup>.Instance);

        Assert.Equal($"https://web.archive.org/web/20171205101112/{Post}", (await lookup.FindAsync(Post, new DateOnly(2017, 12, 2), CancellationToken)).CopyUrl);
    }

    [Fact]
    public async Task TheStandInOnlyHasCopiesOfSamplePostsThatSaySo()
    {
        var lookup = new SampleArchiveLookup();

        Assert.Equal(
            "https://web.archive.org/web/26811205120000/https://ada.links.csadvent.test/gone-archived",
            (await lookup.FindAsync("https://ada.links.csadvent.test/gone-archived", new DateOnly(2681, 12, 5), CancellationToken)).CopyUrl);
        Assert.Equal(ArchiveAnswer.NoCopy, await lookup.FindAsync("https://ada.links.csadvent.test/gone", new DateOnly(2681, 12, 5), CancellationToken));
        Assert.Equal(ArchiveAnswer.NoCopy, await lookup.FindAsync(Post, new DateOnly(2017, 12, 2), CancellationToken));
    }
}
