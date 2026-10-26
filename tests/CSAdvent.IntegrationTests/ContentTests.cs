using System.Net.Sockets;
using System.Text.RegularExpressions;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSAdvent.IntegrationTests;

/// <summary>The About and Other Advents pages, and the organizers' content and list editors.</summary>
public sealed partial class ContentTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheAboutPageHasTheHistoryRulesAndOrganizers()
    {
        using var client = fixture.CreateWebClient();

        var page = await client.GetStringAsync("/about", CancellationToken);

        Assert.Contains("<h2>History</h2>", page);
        Assert.Contains("#csharp and #csadvent", page);
        Assert.Contains("https://github.com/mgroves", page);

        // The photos come from our own site, so visitors' browsers never ask GitHub (#168), and only organizers' are served.
        Assert.Contains("src=\"about/photos/calvinallen\"", page);
        Assert.DoesNotContain(".png?size=", page);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/about/photos/octocat", CancellationToken)).StatusCode);

        // Matt started C# Advent and still hosts it, so he's featured first.
        Assert.Contains("Founder and host</p>", page);
        Assert.Contains("C# Advent is Matt&#x27;s brainchild.", page);
        Assert.True(page.IndexOf("Matt Groves", StringComparison.Ordinal) < page.IndexOf("Calvin Allen", StringComparison.Ordinal));
        Assert.Contains("Co-host</h2>", page);
        Assert.Contains("<meta property=\"og:title\" content=\"About C# Advent\"", page);
        Assert.DoesNotContain("Built with", page);
    }

    [Fact]
    public async Task OrganizersEditCopyAndEverySaveIsKept()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var saved = await organizer.PostFormAsync($"/manage/content/{ContentCatalog.OtherAdvents}", "content", new()
        {
            ["Form.Body"] = "Some **other** calendars, edited by the tests.",
        });

        Assert.Equal($"/manage/content/{ContentCatalog.OtherAdvents}?done=saved", TestBrowser.PathOf(saved.Headers.Location!));
        using var client = fixture.CreateWebClient();
        Assert.Contains("Some <strong>other</strong> calendars, edited by the tests.", await client.GetStringAsync("/others", CancellationToken));

        await using var db = await fixture.CreateDbContextAsync();
        Assert.True(await db.ContentBlockRevisions.AnyAsync(r => r.ContentBlockKey == ContentCatalog.OtherAdvents, CancellationToken));
        Assert.Contains("Load into the editor", await organizer.GetStringAsync($"/manage/content/{ContentCatalog.OtherAdvents}", CancellationToken));
    }

    [Fact]
    public async Task EachSeasonsListCanBeStartedFromSuggestionsAddedToAndTrimmed()
    {
        await TestSeasons.EnsureAsync(fixture, 2116, claimsOpen: false);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        const string page = "/manage/other-advents?year=2116";

        Assert.Contains("This season has no list yet", await organizer.GetStringAsync(page, CancellationToken));
        await organizer.PostFormAsync(page, "copy", new());
        var list = await organizer.GetStringAsync(page, CancellationToken);
        Assert.Contains("Advent of Code", list);

        await organizer.PostFormAsync(page, "add", new() { ["Form.Name"] = "Test Advent 2116", ["Form.Url"] = "https://example.com/advent-2116" });
        Assert.Contains("Test Advent 2116", await organizer.GetStringAsync(page, CancellationToken));

        int id;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            id = await db.OtherAdvents.Where(a => a.SeasonYear == 2116 && a.Name == "Test Advent 2116").Select(a => a.Id).SingleAsync(CancellationToken);
        }

        var html = await organizer.GetStringAsync(page, CancellationToken);
        var token = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value);
        var removed = await organizer.PostAsync("/manage/other-advents/remove", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Year", "2116"), new("Id", $"{id}")]), CancellationToken);
        Assert.Equal(page, TestBrowser.PathOf(removed.Headers.Location!));
        Assert.DoesNotContain("Test Advent 2116", await organizer.GetStringAsync(page, CancellationToken));
    }

    [Fact]
    public async Task TheOtherAdventsPageListsCalendarsAndOnlyOrganizersEditThem()
    {
        using var client = fixture.CreateWebClient();
        var others = await client.GetStringAsync("/others", CancellationToken);
        Assert.Contains("href=\"https://adventofcode.com/\"", others);

        using var contributor = await fixture.SignInAsync("content-nosy", 6501);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf((await contributor.GetAsync("/manage/content", CancellationToken)).Headers.Location!));
    }

    [Fact]
    public async Task OrganizersUploadLogosAndTheSiteServesThemAsItsOwnPngs()
    {
        await TestSeasons.EnsureAsync(fixture, 2117, claimsOpen: false);
        int id;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = await db.Seasons.Include(s => s.OtherAdvents).SingleAsync(s => s.Year == 2117, CancellationToken);
            season.OtherAdvents.Add(new OtherAdvent { Name = "Logo Advent 2117", Url = "https://example.com/logo-advent-2117", SortOrder = 1 });
            await db.SaveChangesAsync(CancellationToken);
            id = season.OtherAdvents.Single().Id;
        }

        try
        {
            await UploadsAndServesALogoAsync(id);
        }
        finally
        {
            // Taken off again: the public page shows the latest list up to the current season, which can be 2117 when this
            // class runs before the classes with earlier seasons, and TheOtherAdventsPageListsCalendarsAndOnlyOrganizersEditThem
            // expects the suggestions.
            await using var db = await fixture.CreateDbContextAsync();
            await db.OtherAdvents.Where(a => a.Id == id).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    private async Task UploadsAndServesALogoAsync(int id)
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        const string page = "/manage/other-advents?year=2117";
        var html = await organizer.GetStringAsync(page, CancellationToken);
        Assert.DoesNotMatch(LogoLink(), html);

        var uploaded = await UploadLogoAsync(organizer, html, 2117, id, Convert.FromBase64String(Png200));
        Assert.Equal($"{page}&logo=uploaded", TestBrowser.PathOf(uploaded.Headers.Location!));

        html = await organizer.GetStringAsync(uploaded.Headers.Location, CancellationToken);
        Assert.Contains("Logo uploaded.", html);
        var link = System.Net.WebUtility.HtmlDecode(LogoLink().Match(html).Value);

        // Redrawn by the site to fit 128 pixels, and safe to cache because the link changes with the logo.
        using var client = fixture.CreateWebClient();
        var logo = await client.GetAsync("/" + link, CancellationToken);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", logo.Headers.GetValues("X-Content-Type-Options").Single());
        var png = await logo.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal(128, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));

        var rejected = await UploadLogoAsync(organizer, html, 2117, id, "<svg/>"u8.ToArray());
        Assert.Equal($"{page}&logo=invalid", TestBrowser.PathOf(rejected.Headers.Location!));
    }

    [Fact]
    public async Task RefreshingALogoNeverReachesAPrivateAddressAndKeepsTheOneItHad()
    {
        // A calendar whose address is on this machine, as an organizer might type one for the container network or the cloud
        // metadata endpoint. A listener there counts every connection Refresh makes to it: there must be none (#199).
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connections = 0;
        var accepting = Task.Run(async () =>
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(CancellationToken);
                Interlocked.Increment(ref connections);
            }
        }, CancellationToken);
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/advent";

        await TestSeasons.EnsureAsync(fixture, 2621, claimsOpen: false);
        int id;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = await db.Seasons.Include(s => s.OtherAdvents).SingleAsync(s => s.Year == 2621, CancellationToken);
            season.OtherAdvents.Add(new OtherAdvent { Name = "Private Advent 2621", Url = url, SortOrder = 1 });
            await db.SaveChangesAsync(CancellationToken);
            id = season.OtherAdvents.Single().Id;
        }

        try
        {
            using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
            const string page = "/manage/other-advents?year=2621";
            var uploaded = await UploadLogoAsync(organizer, await organizer.GetStringAsync(page, CancellationToken), 2621, id, Convert.FromBase64String(Png200));
            Assert.Equal($"{page}&logo=uploaded", TestBrowser.PathOf(uploaded.Headers.Location!));
            var logo = LogoLink().Match(await organizer.GetStringAsync(page, CancellationToken)).Value;

            // Someone who isn't an organizer can't refresh it.
            using (var nosy = await fixture.SignInAsync("content-logo-nosy", 262101))
            {
                var refused = await PostRefreshAsync(nosy, await nosy.GetStringAsync("/announcements", CancellationToken), 2621, id);
                Assert.StartsWith("/access-denied", TestBrowser.PathOf(refused.Headers.Location!));
            }

            // Refresh says it found nothing, never connects, and keeps the uploaded logo.
            var refreshed = await PostRefreshAsync(organizer, await organizer.GetStringAsync(page, CancellationToken), 2621, id);
            Assert.Equal($"{page}&logo=not-found", TestBrowser.PathOf(refreshed.Headers.Location!));
            Assert.Equal(0, Volatile.Read(ref connections));
            Assert.Equal(logo, LogoLink().Match(await organizer.GetStringAsync(page, CancellationToken)).Value);
            await using (var db = await fixture.CreateDbContextAsync())
            {
                var row = await db.OtherAdventLogos.AsNoTracking().SingleAsync(l => l.Url == url, CancellationToken);
                Assert.True(row.IsUploaded);
                Assert.NotNull(row.LastAttemptAt);
            }

            // A calendar that isn't on that season's list is a 404.
            var unknown = await PostRefreshAsync(organizer, await organizer.GetStringAsync(page, CancellationToken), 2621, id + 100_000);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        finally
        {
            // Taken off again, for the same reason as in OrganizersUploadLogosAndTheSiteServesThemAsItsOwnPngs.
            await using var db = await fixture.CreateDbContextAsync();
            await db.OtherAdvents.Where(a => a.Id == id).ExecuteDeleteAsync(CancellationToken.None);
            await db.OtherAdventLogos.Where(l => l.Url == url).ExecuteDeleteAsync(CancellationToken.None);
            listener.Stop();
            await Assert.ThrowsAnyAsync<Exception>(() => accepting);
        }
    }

    [Fact]
    public async Task ReplicasNeverFetchTheSameLogoTwice()
    {
        const string url = "https://example.com/logo-race-2500";
        var handler = new LogoSite();
        await using var first = await fixture.CreateDbContextAsync();
        await using var second = await fixture.CreateDbContextAsync();
        OtherAdventLogoService Replica(AdventDbContext db) =>
            new(db, new LogoFetcher(new HttpClient(handler), NullLogger<LogoFetcher>.Instance), TimeProvider.System);

        try
        {
            // Both add the site's row at once without tripping the unique index, and only one of them claims and fetches it.
            var results = await Task.WhenAll(Replica(first).FetchIfMissingAsync(url, CancellationToken), Replica(second).FetchIfMissingAsync(url, CancellationToken));

            Assert.Equal(new LogoResult?[] { LogoResult.Saved, null }, results.OrderBy(r => r is null));
            Assert.Equal(1, handler.PagesFetched);
            await using var db = await fixture.CreateDbContextAsync();
            Assert.NotNull(await db.OtherAdventLogos.Where(l => l.Url == url).Select(l => l.Png).SingleAsync(CancellationToken));

            // Once it has a logo, the background fetch leaves it alone.
            Assert.Null(await Replica(db).FetchIfMissingAsync(url, CancellationToken));
            Assert.Equal(1, handler.PagesFetched);
        }
        finally
        {
            await using var db = await fixture.CreateDbContextAsync();
            await db.OtherAdventLogos.Where(l => l.Url == url).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    /// <summary>A calendar's site with no icon links on its home page, only a /favicon.ico.</summary>
    private sealed class LogoSite : HttpMessageHandler
    {
        private int _pagesFetched;

        public int PagesFetched => _pagesFetched;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Slow enough that the two replicas' fetches would overlap.
            await Task.Delay(200, cancellationToken);
            if (request.RequestUri!.AbsolutePath == "/favicon.ico")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(Png200)) };
            }

            if (request.RequestUri.AbsolutePath == "/logo-race-2500")
            {
                Interlocked.Increment(ref _pagesFetched);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<html><head></head></html>") };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }
    }

    private static async Task<HttpResponseMessage> PostRefreshAsync(HttpClient browser, string page, int year, int id)
    {
        var token = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);
        return await browser.PostAsync(
            "/manage/other-advents/logo/refresh",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Year", $"{year}"), new("Id", $"{id}")]),
            CancellationToken);
    }

    private static async Task<HttpResponseMessage> UploadLogoAsync(HttpClient browser, string page, int year, int id, byte[] image)
    {
        var token = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent($"{year}"), "Year" },
            { new StringContent($"{id}"), "Id" },
            { new ByteArrayContent(image), "Logo", "logo.png" },
        };
        return await browser.PostAsync("/manage/other-advents/logo/upload", form, CancellationToken);
    }

    /// <summary>A 200×200 PNG in the logo blue.</summary>
    private const string Png200 = "iVBORw0KGgoAAAANSUhEUgAAAMgAAADICAYAAACtWK6eAAABoklEQVR42u3TMQ0AAAjAMHSgCoNoBQMoID1qYMkiqwe4hQhgEDAIGAQMAgYBg4BBwCBgEMAgYBAwCBgEDAIGAYOAQcAgYBAhwCBgEDAIGAQMAgYBg4BBwCCAQcAgYBAwCBgEDAIGAYOAQcAggEHAIGAQMAgYBAwCBgGDgEEAg4BBwCBgEDAIGAQMAgYBg4BBAIOAQcAgYBAwCBgEDAIGAYMABgGDgEHAIGAQMAgYBAwCBgGDAAYBg4BBwCBgEDAIGAQMAgYBg4gABgGDgEHAIGAQMAgYBAwCBgEMAgYBg4BBwCBgEDAIGAQMAgYRAgwCBgGDgEHAIGAQMAgYBAwCGAQMAgYBg4BBwCBgEDAIGAQMAhgEDAIGAYOAQcAgYBAwCBgEMAgYBAwCBgGDgEHAIGAQMAgYBDAIGAQMAgYBg4BBwCBgEDAIYBAwCBgEDAIGAYOAQcAgYBAwCGAQMAgYBAwCBgGDgEHAIGAQMIgIYBAwCBgEDAIGAYOAQcAgYBDAIGAQMAgYBAwCBgGDgEHAIGAQIcAgYBAwCBgEDAIGAYPARwvc0TwF76ncuQAAAABJRU5ErkJggg==";

    [GeneratedRegex(@"others/logos/\d+\.png\?v=\d+")]
    private static partial Regex LogoLink();

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
