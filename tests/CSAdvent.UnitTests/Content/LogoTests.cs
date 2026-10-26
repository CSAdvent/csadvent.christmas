using System.Net;
using Bunit;
using CSAdvent.Web.Components.Layout;
using CSAdvent.Web.Content;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace CSAdvent.UnitTests.Content;

public sealed class LogoTests : BunitContext
{
    private static readonly Uri Site = new("https://advent.example/calendar/");

    [Fact]
    public void PrefersAppleTouchIconsThenTheBiggestIcon()
    {
        const string html = """
            <head>
              <link rel="icon" href="/favicon-16.png" sizes="16x16">
              <link rel="icon" href="/favicon-32.png" sizes="32x32">
              <LINK REL='shortcut icon' href=big.png sizes=192x192>
              <link rel="apple-touch-icon" href="https://cdn.example/touch.png">
            </head>
            """;

        var candidates = LogoFinder.FindCandidates(html, Site).Select(u => u.ToString());

        Assert.Equal(
        [
            "https://cdn.example/touch.png",
            "https://advent.example/calendar/big.png",
            "https://advent.example/favicon-32.png",
            "https://advent.example/favicon-16.png",
            "https://advent.example/apple-touch-icon.png",
            "https://advent.example/favicon.ico",
        ], candidates);
    }

    [Fact]
    public void SkipsSvgIconsAndOtherLinks()
    {
        const string html = """
            <link rel="stylesheet" href="/site.css">
            <link rel="icon" type="image/svg+xml" href="/icon.svg">
            <link rel="mask-icon" href="/mask.svg">
            <link rel="icon" href="data:image/png;base64,AAAA">
            <link rel="icon" href="/icon.png">
            """;

        Assert.Equal(
            ["https://advent.example/icon.png", "https://advent.example/apple-touch-icon.png", "https://advent.example/favicon.ico"],
            LogoFinder.FindCandidates(html, Site).Select(u => u.ToString()));
    }

    [Fact]
    public void ResolvesIconsAgainstTheBaseTag()
    {
        const string html = """<base href="https://static.example/assets/"><link rel="icon" href="icon.png">""";

        Assert.Equal("https://static.example/assets/icon.png", LogoFinder.FindCandidates(html, Site)[0].ToString());
    }

    [Fact]
    public void WithoutAPageItStillTriesTheUsualPlaces()
    {
        Assert.Equal(
            ["https://advent.example/apple-touch-icon.png", "https://advent.example/favicon.ico"],
            LogoFinder.FindCandidates("", Site).Select(u => u.ToString()));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("140.82.112.3", true)]
    [InlineData("2606:4700::6810:84e5", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("168.63.129.16", false)]      // Azure's host (#192)
    [InlineData("168.63.129.17", true)]
    [InlineData("::10.0.0.1", false)]         // IPv4-compatible (::/96)
    [InlineData("::a00:1", false)]
    [InlineData("64:ff9b::a00:1", false)]     // NAT64 (64:ff9b::/96)
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("64:ff9b:1::a00:1", false)]   // local-use NAT64 (64:ff9b:1::/48)
    [InlineData("2002:a00:1::1", false)]      // 6to4 (2002::/16)
    [InlineData("2001:4860:4860::8888", true)]
    public void OnlyFetchesFromPublicAddresses(string address, bool isPublic) =>
        Assert.Equal(isPublic, PublicAddresses.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public void NeverFetchesThroughAProxy()
    {
        // Through a proxy, the address checked would be the proxy's, and the proxy would reach the site for us (#192).
        using var handler = PublicAddresses.CreateHandler();

        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public void RedrawsBigImagesToFit128Pixels()
    {
        var png = LogoImages.ToPng(Image(300, 150), out var size);

        Assert.Equal(300, size);
        using var logo = SKBitmap.Decode(png);
        Assert.Equal((128, 64), (logo.Width, logo.Height));
    }

    [Fact]
    public void KeepsSmallImagesAtTheirSize()
    {
        var png = LogoImages.ToPng(Image(32, 32), out var size);

        Assert.Equal(32, size);
        using var logo = SKBitmap.Decode(png);
        Assert.Equal(32, logo.Width);
    }

    [Fact]
    public void RefusesAnythingThatIsNotASensibleImage()
    {
        Assert.Null(LogoImages.ToPng("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), out _));
        Assert.Null(LogoImages.ToPng(Image(5000, 10), out _));
    }

    [Fact]
    public async Task FetchesTheDeclaredIcon()
    {
        var fetcher = Fetcher(new()
        {
            ["https://advent.example/calendar/"] = Html("""<link rel="apple-touch-icon" href="/touch.png">"""),
            ["https://advent.example/touch.png"] = Png(180),
        });

        var logo = await fetcher.FetchAsync(Site, Xunit.TestContext.Current.CancellationToken);

        using var bitmap = SKBitmap.Decode(logo);
        Assert.Equal(128, bitmap.Width);
    }

    [Fact]
    public async Task FallsBackToTheFaviconWhenThePageFails()
    {
        var fetcher = Fetcher(new() { ["https://advent.example/favicon.ico"] = Png(48) });

        var logo = await fetcher.FetchAsync(Site, Xunit.TestContext.Current.CancellationToken);

        using var bitmap = SKBitmap.Decode(logo);
        Assert.Equal(48, bitmap.Width);
    }

    [Fact]
    public async Task KeepsTheBiggestWhenNoneIsBigEnough()
    {
        var fetcher = Fetcher(new()
        {
            ["https://advent.example/calendar/"] = Html("""<link rel="icon" href="/broken.png" sizes="64x64"><link rel="icon" href="/small.png" sizes="16x16">"""),
            ["https://advent.example/broken.png"] = () => new HttpResponseMessage { Content = new StringContent("not an image") },
            ["https://advent.example/small.png"] = Png(16),
            ["https://advent.example/favicon.ico"] = Png(32),
        });

        var logo = await fetcher.FetchAsync(Site, Xunit.TestContext.Current.CancellationToken);

        using var bitmap = SKBitmap.Decode(logo);
        Assert.Equal(32, bitmap.Width);
    }

    [Fact]
    public async Task FindsNothingOnASiteWithoutIcons()
    {
        var fetcher = Fetcher(new() { ["https://advent.example/calendar/"] = Html("<p>No icons here.</p>") });

        Assert.Null(await fetcher.FetchAsync(Site, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ShowsTheLogoOrTheFirstLetter()
    {
        var withLogo = Render<AdventLogo>(p => p.Add(c => c.Item, new OtherAdventItem(1, "Advent of Code", "https://adventofcode.com/", null, "others/logos/1.png?v=5")));
        var withoutLogo = Render<AdventLogo>(p => p.Add(c => c.Item, new OtherAdventItem(2, ".NET Advent Calendar", "https://dotnet.christmas/", null)));

        Assert.Equal("others/logos/1.png?v=5", withLogo.Find("img").GetAttribute("src"));
        Assert.Equal("N", withoutLogo.Find("span").TextContent);
    }

    [Fact]
    public void ShowsTheSiteWithoutTheScheme()
    {
        Assert.Equal("adventofcode.com", new OtherAdventItem(null, "Advent of Code", "https://adventofcode.com/", null).Host);
        Assert.Equal("sergeytihon.com/fsadvent", new OtherAdventItem(null, "F#", "https://sergeytihon.com/fsadvent/", null).Host);
        Assert.Equal("javaadvent.com/calendar", new OtherAdventItem(null, "Java", "https://www.javaadvent.com/calendar", null).Host);
    }

    private static byte[] Image(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static Func<HttpResponseMessage> Png(int size) => () =>
        new HttpResponseMessage { Content = new ByteArrayContent(Image(size, size)) };

    private static Func<HttpResponseMessage> Html(string html) => () =>
        new HttpResponseMessage { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };

    private static LogoFetcher Fetcher(Dictionary<string, Func<HttpResponseMessage>> responses) =>
        new(new HttpClient(new FakeSites(responses)), NullLogger<LogoFetcher>.Instance);

    /// <summary>Answers from a fixed set of URLs, and 404 for anything else.</summary>
    private sealed class FakeSites(Dictionary<string, Func<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responses.TryGetValue(request.RequestUri!.ToString(), out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
