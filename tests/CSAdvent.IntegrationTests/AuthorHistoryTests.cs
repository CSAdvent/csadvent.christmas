using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Organizers link archive authors to GitHub accounts and edit their profiles, and those authors see their history. Uses
/// season 2123, and renames a few 2024 archive authors (no other test reads 2024's names).
/// </summary>
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

        var linked = await LinkAsync(organizer, AuthorService.Key(name), "@History-Ada");
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

        var profile = $"{Page}/{await ContributorIdAsync(8401)}";
        var unconfirmed = await organizer.PostFormAsync(profile, "unlink", new());
        Assert.Contains("Tick the box to confirm you want to unlink them.", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));

        var unlinked = await organizer.PostFormAsync(profile, "unlink", new() { ["Unlink.Confirm"] = "true" });
        Assert.Equal($"{Page}?done=unlinked", TestBrowser.PathOf(unlinked.Headers.Location!));
        Assert.DoesNotContain("Your C# Advent history", await author.GetStringAsync("/my-spots", CancellationToken));
    }

    [Fact]
    public async Task OrganizersCombineAnAuthorsNamesAndPutOneNameOnAllTheirPosts()
    {
        var (first, second) = await RenameArchivePostsAsync(18, "Quinn Combine", "Q. Combine");
        using var author = await fixture.SignInAsync("quinn-combine", 8403);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // They've signed in, so linking finds them without asking GitHub.
        await LinkAsync(organizer, "quinn combine", "quinn-combine");
        var contributorId = await ContributorIdAsync(8403);
        var profile = $"{Page}/{contributorId}";
        Assert.Matches(@"<option value=""q\. combine"">Q\. Combine \(2024\)[^<]*suggested</option>", await organizer.GetPageAsync(profile));

        var combined = await organizer.PostFormAsync(profile, "combine", new() { ["Combine.Key"] = "q. combine" });
        Assert.Equal($"{profile}?done=combined", TestBrowser.PathOf(combined.Headers.Location!));

        var saved = await organizer.PostFormAsync(profile, "profile", new()
        {
            ["Form.Name"] = "Quinn Combine",
            ["Form.Link"] = "https://example.com/quinn",
            ["Form.Mastodon"] = "@quinn@mastodon.social",
            ["Form.UpdatePosts"] = "true",
        });
        Assert.Equal($"{profile}?done=saved", TestBrowser.PathOf(saved.Headers.Location!));

        await using var db = await fixture.CreateDbContextAsync();
        var posts = await db.Slots.AsNoTracking().Where(s => s.Id == first || s.Id == second).ToListAsync(CancellationToken);
        Assert.All(posts, post => Assert.Equal((contributorId, "Quinn Combine", "https://example.com/quinn"), (post.ContributorId, post.AuthorName, post.AuthorUrl)));
        Assert.Equal("@quinn@mastodon.social", (await db.Contributors.AsNoTracking().SingleAsync(c => c.Id == contributorId, CancellationToken)).MastodonHandle);
    }

    [Fact]
    public async Task OrganizersRenameAnArchiveAuthorWhoIsNotLinked()
    {
        var (slotId, _) = await RenameArchivePostsAsync(21, "Rena Rename");
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = $"{Page}/unlinked?key=rena%20rename";
        Assert.Contains(">Rena Rename</h1>", await organizer.GetStringAsync(page, CancellationToken));

        var refused = await organizer.PostFormAsync(page, "link", new() { ["Link.Login"] = "not a login!" });
        Assert.Contains("There&#x27;s no GitHub account with that login.", await refused.Content.ReadAsStringAsync(CancellationToken));

        var renamed = await organizer.PostFormAsync(page, "rename", new() { ["Rename.Name"] = "Rena Renamed", ["Rename.Link"] = "https://example.com/rena" });
        Assert.Equal($"{Page}/unlinked?key=rena%20renamed&done=renamed", TestBrowser.PathOf(renamed.Headers.Location!));

        await using var db = await fixture.CreateDbContextAsync();
        var slot = await db.Slots.AsNoTracking().SingleAsync(s => s.Id == slotId, CancellationToken);
        Assert.Equal(("Rena Renamed", "https://example.com/rena"), (slot.AuthorName, slot.AuthorUrl));
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

        var refused = await LinkAsync(organizer, AuthorService.Key(name), "not a login!");

        Assert.Equal($"{Page}?done=no-account", TestBrowser.PathOf(refused.Headers.Location!));
    }

    [Theory]
    [InlineData(Page)]
    [InlineData($"{Page}/unlinked?key=anyone")]
    public async Task OnlyOrganizersSeeTheAuthors(string path)
    {
        using var contributor = await fixture.SignInAsync("authors-nosy", 8402);

        var response = await contributor.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(response.Headers.Location!));
    }

    /// <summary>
    /// Gives published 2024 posts from the day on (three days for each test) names only this test uses, as unlinked archive
    /// authors. Returns the first two posts.
    /// </summary>
    private async Task<(int First, int Second)> RenameArchivePostsAsync(int fromDay, params string[] names)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var slots = await db.Slots
            .Where(s => s.SeasonYear == 2024 && s.Day >= fromDay && s.Day < fromDay + 3 && s.PostUrl != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Take(names.Length)
            .ToListAsync(CancellationToken);
        foreach (var (slot, name) in slots.Zip(names))
        {
            slot.AuthorName = name;
            slot.ContributorId = null;
        }

        await db.SaveChangesAsync(CancellationToken);
        return (slots[0].Id, slots.ElementAtOrDefault(1)?.Id ?? 0);
    }

    private async Task<int> ContributorIdAsync(long githubUserId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
    }

    /// <summary>Posts an unlinked author's Link button on the authors page.</summary>
    private async Task<HttpResponseMessage> LinkAsync(HttpClient organizer, string key, string login)
    {
        var page = await organizer.GetStringAsync(Page, CancellationToken);
        var token = WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Key"] = key, ["Login"] = login };
        return await organizer.PostAsync("/manage/authors/link", new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
