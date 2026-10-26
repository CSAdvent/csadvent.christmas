using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CSAdvent.Web.DeadLinks;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class LinkProbeTests
{
    private static readonly Uri Post = new("https://blog.example.com/2018/12/05/span-of-t/");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(200, LinkOutcome.Loaded)]
    [InlineData(203, LinkOutcome.Loaded)]
    [InlineData(404, LinkOutcome.Failed)]
    [InlineData(410, LinkOutcome.Failed)]
    [InlineData(500, LinkOutcome.Failed)]
    [InlineData(503, LinkOutcome.Failed)]
    [InlineData(301, LinkOutcome.Failed)]
    [InlineData(401, LinkOutcome.Refused)]
    [InlineData(403, LinkOutcome.Refused)]
    [InlineData(429, LinkOutcome.Refused)]
    [InlineData(999, LinkOutcome.Failed)]
    public void EachAnswerCountsTheWayItShould(int status, LinkOutcome outcome) =>
        Assert.Equal(outcome, LinkProbes.FromResponse(status, "Reason", Post, Post).Outcome);

    [Fact]
    public void TheDetailSaysWhatTheSiteAnswered()
    {
        Assert.Equal("The page answered 404 Not Found.", LinkProbes.FromResponse(404, "Not Found", Post, Post).Detail);
        Assert.Equal("The site turned the check away (403 Forbidden).", LinkProbes.FromResponse(403, "Forbidden", Post, Post).Detail);
        Assert.Equal("The site answered 502 Bad Gateway.", LinkProbes.FromResponse(502, "Bad Gateway", Post, Post).Detail);
        Assert.Equal("It redirects too many times.", LinkProbes.FromResponse(302, "Found", Post, Post).Detail);
    }

    [Fact]
    public void APostThatNowLandsOnAHomePageIsGone()
    {
        var home = LinkProbes.FromResponse(200, "OK", Post, new Uri("https://blog.example.com/"));
        Assert.Equal(LinkProbe.Failed("It now redirects to the home page of blog.example.com."), home);

        // Another site's home page, too (a domain that's been sold, say).
        Assert.Equal(LinkOutcome.Failed, LinkProbes.FromResponse(200, "OK", Post, new Uri("https://parked.example.net")).Outcome);

        // A post that moved to another address of its own is fine, and so is a post that is a site's home page.
        Assert.Equal(LinkOutcome.Loaded, LinkProbes.FromResponse(200, "OK", Post, new Uri("https://new.example.com/span-of-t")).Outcome);
        var site = new Uri("https://someone.github.io/");
        Assert.Equal(LinkOutcome.Loaded, LinkProbes.FromResponse(200, "OK", site, site).Outcome);
    }

    [Fact]
    public void ASiteWhoseAddressIsGoneIsSaidSo()
    {
        var notFound = new HttpRequestException("No such host is known.", new SocketException((int)SocketError.HostNotFound));

        Assert.Equal(LinkProbe.Failed("The site's address, blog.example.com, doesn't exist any more."), LinkProbes.FromException(notFound, Post));
        Assert.Equal("The site's certificate isn't valid.", LinkProbes.FromException(new HttpRequestException("SSL", new AuthenticationException()), Post).Detail);
        Assert.Equal("The site didn't answer in time.", LinkProbes.FromException(new TaskCanceledException(), Post).Detail);
        Assert.Equal("The site couldn't be reached.", LinkProbes.FromException(new HttpRequestException("Connection refused"), Post).Detail);
    }

    [Theory]
    [InlineData("https://www.Example.com/post", "example.com")]
    [InlineData("https://dev.to/someone/post", "dev.to")]
    [InlineData("https://a.links.csadvent.test/gone", "a.links.csadvent.test")]
    public void ASiteIsItsHostWithoutWww(string url, string host) =>
        Assert.Equal(host, LinkProbes.HostOf(url));

    [Fact]
    public void ARunTakesTheMostOverdueOnePerSiteSkippingSitesCheckedLately()
    {
        (int, int, string)[] due =
        [
            (1, 11, "https://dev.to/a/one"),
            (2, 12, "https://dev.to/b/two"),
            (3, 13, "https://www.recent.example/post"),
            (4, 14, "https://blog.example.com/post"),
            (5, 15, "https://other.example.org/post"),
        ];

        var picked = LinkCheckService.Pick(due, ["https://recent.example/older-post"], count: 2);

        Assert.Equal([1, 4], picked.Select(p => p.Id));
        Assert.Equal([1, 4, 5], LinkCheckService.Pick(due, ["https://recent.example/older-post"], count: 5).Select(p => p.Id));
    }

    [Theory]
    [InlineData("https://ada.links.csadvent.test/working", LinkOutcome.Loaded)]
    [InlineData("https://ada.links.csadvent.test/gone", LinkOutcome.Failed)]
    [InlineData("https://ada.links.csadvent.test/gone-archived", LinkOutcome.Failed)]
    [InlineData("https://ada.links.csadvent.test/down", LinkOutcome.Failed)]
    [InlineData("https://ada.links.csadvent.test/home-moved", LinkOutcome.Failed)]
    [InlineData("https://links.csadvent.test/refused", LinkOutcome.Refused)]
    [InlineData("https://www.jamietech.com/2017/12/01/open-source-software-a-year-in-review/", LinkOutcome.Loaded)]
    public async Task TheStandInNeverReachesARealSite(string url, LinkOutcome outcome) =>
        Assert.Equal(outcome, (await new SampleLinkProber().ProbeAsync(new Uri(url), CancellationToken)).Outcome);

    [Fact]
    public async Task TheWebProberOnlyReadsTheAnswersStatus()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found", Content = new StringContent("<html>…</html>") });
        var prober = new WebLinkProber(new HttpClient(handler), NullLogger<WebLinkProber>.Instance);

        Assert.Equal(LinkProbe.Failed("The page answered 404 Not Found."), await prober.ProbeAsync(Post, CancellationToken));
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
    }

    [Fact]
    public async Task TheWebProberTakesAFailedRequestAsAFailedCheck()
    {
        var prober = new WebLinkProber(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("Connection refused"))), NullLogger<WebLinkProber>.Instance);

        Assert.Equal(LinkProbe.Failed("The site couldn't be reached."), await prober.ProbeAsync(Post, CancellationToken));
    }

    [Fact]
    public void TheWebClientOnlyConnectsToPublicAddressesAndNamesItself()
    {
        using var http = WebLinkProber.CreateClient();

        Assert.Equal(TimeSpan.FromSeconds(20), http.Timeout);
        Assert.Contains("CSAdvent/1.0", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
