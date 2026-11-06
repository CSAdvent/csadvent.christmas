using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Inviting and removing organizers on /manage/organizers: no seasons, GitHub users from 271001, and logins starting
/// "organizing-".
/// </summary>
public sealed partial class OrganizerTests(AppHostFixture fixture)
{
    private const string Page = "/manage/organizers";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnInvitationMakesSomeoneAnOrganizerTheNextTimeTheySignIn()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        using var signedInBefore = await fixture.SignInAsync("organizing-olga", 271001);

        var invited = await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "@Organizing-Olga" });
        Assert.Equal($"{Page}?done=invited", TestBrowser.PathOf(invited.Headers.Location!));
        Assert.Contains("hasn't signed in since being invited", await organizer.GetPageAsync(Page));

        // Her sign-in from before the invitation isn't an organizer's: the invitation only becomes hers when she signs in.
        Assert.False(await IsOrganizerAsync(signedInBefore));
        using var olga = await fixture.SignInAsync("organizing-olga", 271001);
        Assert.True(await IsOrganizerAsync(olga));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(271001, (await db.Organizers.SingleAsync(o => o.GithubLogin == "organizing-olga", CancellationToken)).GithubUserId);
        }

        // From then on it's hers by GitHub user ID: someone else who registers her old login gets nothing, and she keeps it
        // under her new one.
        using var newOwner = await fixture.SignInAsync("organizing-olga", 271002);
        Assert.False(await IsOrganizerAsync(newOwner));
        using var renamed = await fixture.SignInAsync("organizing-olga-renamed", 271001);
        Assert.True(await IsOrganizerAsync(renamed));
        Assert.Contains("invited as organizing-olga", await organizer.GetPageAsync(Page));

        // Inviting her new login changes nothing: she's an organizer already.
        var again = await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "https://github.com/organizing-olga-renamed" });
        Assert.Equal($"{Page}?done=already", TestBrowser.PathOf(again.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.False(await db.Organizers.AnyAsync(o => o.GithubLogin == "organizing-olga-renamed", CancellationToken));
        }
    }

    [Fact]
    public async Task RemovingAnOrganizerTakesAwayTheirAccessAtOnce()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "organizing-rob" });
        using var rob = await fixture.SignInAsync("organizing-rob", 271003);
        Assert.True(await IsOrganizerAsync(rob));

        var removed = await RemoveAsync(organizer, await IdOfAsync("organizing-rob"));

        Assert.Equal($"{Page}?done=removed", TestBrowser.PathOf(removed.Headers.Location!));
        Assert.False(await IsOrganizerAsync(rob));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.Organizers.AnyAsync(o => o.GithubUserId == 271003, CancellationToken));
    }

    [Fact]
    public async Task AnInvitationRemovedBeforeItsUsedGivesNobodyAnything()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "organizing-wendy" });

        await RemoveAsync(organizer, await IdOfAsync("organizing-wendy"));

        using var wendy = await fixture.SignInAsync("organizing-wendy", 271004);
        Assert.False(await IsOrganizerAsync(wendy));
    }

    [Fact]
    public async Task OrganizersCantRemoveThemselvesOrTheDefaultOrganizers()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "organizing-sue" });
        using var sue = await fixture.SignInAsync("organizing-sue", 271005);
        var id = await IdOfAsync("organizing-sue");

        // Her own row has no Remove button, and the default organizers' have none at all.
        var page = await sue.GetPageAsync(Page);
        Assert.DoesNotContain($"name=\"Id\" value=\"{id}\"", page);
        var defaults = page[page.IndexOf("<h2 class=\"text-xl font-semibold text-slate-900\">Default</h2>", StringComparison.Ordinal)..page.IndexOf("<h2 class=\"text-xl font-semibold text-slate-900\">Invited</h2>", StringComparison.Ordinal)];
        Assert.Contains("https://github.com/organizer\"", defaults);
        Assert.DoesNotContain("<form", defaults);

        var yourself = await RemoveAsync(sue, id);

        Assert.Equal($"{Page}?done=yourself", TestBrowser.PathOf(yourself.Headers.Location!));
        Assert.True(await IsOrganizerAsync(sue));
    }

    [Fact]
    public async Task OnlyOrganizersCanInviteOrRemoveOrganizers()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "organizing-vic" });
        var id = await IdOfAsync("organizing-vic");
        using var stranger = await fixture.SignInAsync("organizing-stranger", 271006);

        // A valid token of their own, from the sign-out button every page has.
        var removed = await stranger.PostAsync(
            $"{Page}/remove",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(await stranger.GetPageAsync("/about")), ["Id"] = $"{id}" }),
            CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(removed.Headers.Location!), StringComparison.Ordinal);
        Assert.False(await IsOrganizerAsync(stranger));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.True(await db.Organizers.AnyAsync(o => o.Id == id, CancellationToken));
    }

    [Fact]
    public async Task OnlyGitHubLoginsCanBeInvited()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var invited = await organizer.PostFormAsync(Page, "invite", new() { ["Invite.Login"] = "not a login!" });

        Assert.Equal($"{Page}?done=not-a-login", TestBrowser.PathOf(invited.Headers.Location!));
    }

    /// <summary>Whether the browser's sign-in can open an organizer page, rather than being sent to "Access denied".</summary>
    private static async Task<bool> IsOrganizerAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync(Page, CancellationToken);
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<int> IdOfAsync(string invitedAs)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Organizers.Where(o => o.GithubLogin == invitedAs).Select(o => o.Id).SingleAsync(CancellationToken);
    }

    private static async Task<HttpResponseMessage> RemoveAsync(HttpClient browser, int id) =>
        await browser.PostAsync(
            $"{Page}/remove",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = Token(await browser.GetPageAsync(Page)), ["Id"] = $"{id}" }),
            CancellationToken);

    private static string Token(string page) =>
        WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
