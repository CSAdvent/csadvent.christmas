using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Organizers link archive authors to GitHub accounts, and those authors see their history. Uses season 2123.</summary>
public sealed partial class AuthorHistoryTests(AppHostFixture fixture)
{
    private const string Page = "/manage/authors";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LinkedAuthorsSeeTheirHistoryAndOrganizersSeeThemReturn()
    {
        // An author from the 2017 archive, and a contributor who's signed in.
        string name;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            name = await db.Slots.Where(s => s.SeasonYear == 2017 && s.PostUrl != null && s.AuthorName != null).OrderBy(s => s.Day).Select(s => s.AuthorName!).FirstAsync(CancellationToken);
        }

        using var author = await fixture.SignInAsync("history-ada", 8401);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        Assert.DoesNotContain("Your C# Advent history", await author.GetStringAsync("/my-spots", CancellationToken));

        var linked = await PostAsync(organizer, "/manage/authors/link", AuthorLinkService.Key(name), "@History-Ada");
        Assert.Equal($"{Page}?done=linked", TestBrowser.PathOf(linked.Headers.Location!));

        var mySpots = await author.GetStringAsync("/my-spots", CancellationToken);
        Assert.Contains("Your C# Advent history", mySpots);
        Assert.Contains("href=\"archive/2017\"", mySpots);
        Assert.Contains("Welcome back!", mySpots);

        // Their next request says they've written before.
        await TestSeasons.EnsureAsync(fixture, 2123, claimsOpen: true);
        await author.PostFormAsync("/claim/2123/5/1", "claim", new()
        {
            ["Form.DisplayName"] = "Ada Returns",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });
        Assert.Matches("Ada Returns\\s*<span[^>]*>Returning · [0-9]+ posts?</span>", await organizer.GetStringAsync("/manage/requests?year=2123", CancellationToken));

        var unlinked = await PostAsync(organizer, "/manage/authors/unlink", AuthorLinkService.Key(name), null);
        Assert.Equal($"{Page}?done=unlinked", TestBrowser.PathOf(unlinked.Headers.Location!));
        Assert.DoesNotContain("Your C# Advent history", await author.GetStringAsync("/my-spots", CancellationToken));
    }

    [Fact]
    public async Task SomethingThatCannotBeALoginIsRefusedWithoutAskingGitHub()
    {
        string name;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            name = await db.Slots.Where(s => s.SeasonYear == 2018 && s.PostUrl != null && s.AuthorName != null).OrderBy(s => s.Day).Select(s => s.AuthorName!).FirstAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var refused = await PostAsync(organizer, "/manage/authors/link", AuthorLinkService.Key(name), "not a login!");

        Assert.Equal($"{Page}?done=no-account", TestBrowser.PathOf(refused.Headers.Location!));
    }

    [Fact]
    public async Task OnlyOrganizersSeeTheAuthors()
    {
        using var contributor = await fixture.SignInAsync("authors-nosy", 8402);

        var response = await contributor.GetAsync(Page, CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(response.Headers.Location!));
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient organizer, string path, string key, string? login)
    {
        var page = await organizer.GetStringAsync(Page, CancellationToken);
        var token = WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);
        var fields = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("Key", key) };
        if (login is not null)
        {
            fields.Add(new("Login", login));
        }

        return await organizer.PostAsync(path, new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
