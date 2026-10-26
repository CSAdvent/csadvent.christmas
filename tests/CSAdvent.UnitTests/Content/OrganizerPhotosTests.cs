using System.Net;
using System.Net.Http.Headers;
using CSAdvent.Web.Content;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Content;

public sealed class OrganizerPhotosTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FetchesAnOrganizersPhotoOnceADay()
    {
        var github = new FakeGitHub(_ => Image(Jpeg, "image/jpeg"));
        var photos = Photos(github);

        var photo = await photos.GetAsync("CALVINALLEN", TestContext.Current.CancellationToken);
        await photos.GetAsync("calvinallen", TestContext.Current.CancellationToken);

        Assert.Equal(Jpeg, photo!.Image);
        Assert.Equal("image/jpeg", photo.ContentType);
        Assert.Equal(["https://github.com/CalvinAllen.png?size=256"], github.Requests);

        _clock.Advance(TimeSpan.FromDays(1));
        await photos.GetAsync("calvinallen", TestContext.Current.CancellationToken);
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public async Task NeverFetchesAnyoneElses()
    {
        var github = new FakeGitHub(_ => Image(Jpeg, "image/jpeg"));

        Assert.Null(await Photos(github).GetAsync("octocat", TestContext.Current.CancellationToken));
        Assert.Empty(github.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/png")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    [InlineData(HttpStatusCode.OK, "image/svg+xml")]
    public async Task ServesNothingButAPhoto(HttpStatusCode status, string contentType)
    {
        var github = new FakeGitHub(_ => new HttpResponseMessage(status) { Content = Content([1, 2, 3], contentType) });
        var photos = Photos(github);

        Assert.Null(await photos.GetAsync("mgroves", TestContext.Current.CancellationToken));

        // A failure is tried again after a few minutes, not on every visit.
        await photos.GetAsync("mgroves", TestContext.Current.CancellationToken);
        Assert.Single(github.Requests);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await photos.GetAsync("mgroves", TestContext.Current.CancellationToken);
        Assert.Equal(2, github.Requests.Count);
    }

    [Fact]
    public void LinksToOurOwnSite() => Assert.Equal("about/photos/calvinallen", OrganizerPhotos.Link("CalvinAllen"));

    private OrganizerPhotos Photos(FakeGitHub github) => new(
        new HttpClient(github),
        new StaticOptionsMonitor(new SiteOptions
        {
            Organizers = [new Organizer { Name = "Matt Groves", GitHub = "mgroves", Founder = true }, new Organizer { Name = "Calvin Allen", GitHub = "CalvinAllen" }],
        }),
        _clock);

    private static HttpResponseMessage Image(byte[] image, string contentType) => new(HttpStatusCode.OK) { Content = Content(image, contentType) };

    private static ByteArrayContent Content(byte[] body, string contentType) =>
        new(body) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } };

    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StaticOptionsMonitor(SiteOptions value) : IOptionsMonitor<SiteOptions>
    {
        public SiteOptions CurrentValue => value;

        public SiteOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SiteOptions, string?> listener) => null;
    }
}
