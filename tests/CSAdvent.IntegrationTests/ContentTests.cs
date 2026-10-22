using System.Text.RegularExpressions;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

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
        Assert.Contains("https://github.com/CalvinAllen.png", page);
        Assert.Contains("https://github.com/mgroves", page);

        // Matt started C# Advent and still hosts it, so he's featured first.
        Assert.Contains("Founder and host</p>", page);
        Assert.Contains("C# Advent is Matt&#x27;s brainchild.", page);
        Assert.True(page.IndexOf("Matt Groves", StringComparison.Ordinal) < page.IndexOf("Calvin Allen", StringComparison.Ordinal));
        Assert.Contains("Co-host</h2>", page);
        Assert.Contains("<meta property=\"og:title\" content=\"About C# Advent\"", page);
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
