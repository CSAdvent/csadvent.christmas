using System.Text.RegularExpressions;
using CSAdvent.Web.Content;
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

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
