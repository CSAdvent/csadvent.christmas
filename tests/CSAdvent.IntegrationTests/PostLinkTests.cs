using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Authors ask for new links to their older posts, and organizers approve, decline, or change the links themselves. Renames a
/// few 2019 archive posts from December 10 on to names of its own (no other test reads those names).
/// </summary>
public sealed partial class PostLinkTests(AppHostFixture fixture)
{
    private const string Organizers = "hello@csadvent.christmas";

    private const string Page = "/my-spots/links";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AuthorsAskForNewLinksAndOrganizersApprove()
    {
        using var lin = await fixture.SignInAsync("lin-mover", 8801);
        var (moved, kept) = await GiveAsync(10, 12, "Lin Mover", 8801);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        const string newUrl = "https://lin.example.dev/blog/moved-post";

        Assert.Contains($"href=\"{Page.TrimStart('/')}\"", await lin.GetPageAsync("/my-spots"));

        var page = WebUtility.HtmlDecode(await lin.GetPageAsync(Page));
        Assert.Contains(moved.PostTitle!, page);
        Assert.Contains(kept.PostTitle!, page);
        Assert.Contains("name=\"Form.Posts[0].Url\"", page);

        var fields = LinkFields(page, new() { [moved.Id] = newUrl });
        fields["Form.Note"] = "I moved my blog to my own domain.";
        var requested = await lin.PostFormAsync(Page, "post-links", fields);
        Assert.Equal($"{Page}?done=requested", TestBrowser.PathOf(requested.Headers.Location!));

        var waiting = WebUtility.HtmlDecode(await lin.GetPageAsync(Page));
        Assert.Contains("Waiting for an organizer", waiting);
        Assert.Contains($"To {newUrl}", waiting);
        Assert.Contains("You've asked for new links to 1 post", WebUtility.HtmlDecode(await lin.GetPageAsync("/my-spots")));

        await fixture.WaitForEmailAsync(Organizers, "New post links? lin-mover asked to change the links to 1 post");

        var requestId = await PendingIdAsync("lin-mover");
        Assert.Contains("manage/authors/link-changes", await organizer.GetPageAsync("/manage/authors"));
        Assert.Contains("lin-mover", await organizer.GetPageAsync("/manage/authors/link-changes"));

        var review = $"/manage/authors/link-changes/{requestId}";
        var reviewPage = WebUtility.HtmlDecode(await organizer.GetPageAsync(review));
        Assert.Contains(moved.PostUrl!, reviewPage);
        Assert.Contains(newUrl, reviewPage);
        Assert.Contains("I moved my blog to my own domain.", reviewPage);
        Assert.DoesNotContain(kept.PostTitle!, reviewPage);

        var approved = await organizer.PostFormAsync(review, "approve", new());
        Assert.Equal("/manage/authors/link-changes?done=approved", TestBrowser.PathOf(approved.Headers.Location!));
        await fixture.WaitForEmailAsync("lin-mover@example.com", "Your C# Advent posts' links are updated");

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(newUrl, await db.Slots.Where(s => s.Id == moved.Id).Select(s => s.PostUrl).SingleAsync(CancellationToken));
            Assert.Equal(kept.PostUrl, await db.Slots.Where(s => s.Id == kept.Id).Select(s => s.PostUrl).SingleAsync(CancellationToken));
            var request = await db.PostLinkRequests.Include(r => r.Changes).SingleAsync(r => r.Id == requestId, CancellationToken);
            Assert.Equal(PostLinkRequestStatus.Approved, request.Status);
            Assert.Equal("organizer", request.DecidedBy);
            Assert.Equal([(moved.Id, moved.PostUrl!, newUrl)], request.Changes.Select(c => (c.SlotId, c.OldUrl, c.NewUrl)));
        }

        // Readers following our links go to the new address.
        using var reader = fixture.CreateBrowser();
        var go = await reader.GetAsync($"/go/2019/{moved.Day}/{moved.Part}", CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, go.StatusCode);
        Assert.Equal(newUrl, go.Headers.Location!.OriginalString);

        // They can ask again once it's decided.
        Assert.Contains("Ask for the new links", await lin.GetPageAsync(Page));
    }

    [Fact]
    public async Task AuthorsWithdrawToFixARequestAndOrganizersDecline()
    {
        using var wes = await fixture.SignInAsync("wes-withdraw", 8802);
        var (post, _) = await GiveAsync(13, 15, "Wes Withdraw", 8802);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // Every link has to be a web address, and something has to change.
        var invalid = await wes.PostFormAsync(Page, "post-links", LinkFields(await wes.GetPageAsync(Page), new() { [post.Id] = "wes.example.dev/no-scheme" }));
        Assert.Contains("“wes.example.dev/no-scheme” isn't a full web address.", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync(CancellationToken)));
        var unchanged = await wes.PostFormAsync(Page, "post-links", LinkFields(await wes.GetPageAsync(Page), new()));
        Assert.Contains("All of those links are the same as they were.", WebUtility.HtmlDecode(await unchanged.Content.ReadAsStringAsync(CancellationToken)));

        // A typo: they withdraw it and ask again. Asking while one is waiting doesn't add another.
        await wes.PostFormAsync(Page, "post-links", LinkFields(await wes.GetPageAsync(Page), new() { [post.Id] = "https://wes.example.dev/tpyo" }));
        var withdrawnId = await PendingIdAsync("wes-withdraw");
        var again = await wes.PostFormAsync(Page, "post-links", new() { ["Form.Note"] = "Asking twice." });
        Assert.Equal($"{Page}?done=requested", TestBrowser.PathOf(again.Headers.Location!));

        var withdrawn = await wes.PostFormAsync(Page, "withdraw", new());
        Assert.Equal($"{Page}?done=withdrawn", TestBrowser.PathOf(withdrawn.Headers.Location!));
        await wes.PostFormAsync(Page, "post-links", LinkFields(await wes.GetPageAsync(Page), new() { [post.Id] = "https://wes.example.dev/typo" }));
        var requestId = await PendingIdAsync("wes-withdraw");

        var review = $"/manage/authors/link-changes/{requestId}";
        Assert.Contains("https://wes.example.dev/typo", await organizer.GetPageAsync(review));
        Assert.Contains("Withdrawn by them", await organizer.GetPageAsync($"/manage/authors/link-changes/{withdrawnId}"));

        var declined = await organizer.PostFormAsync(review, "decline", new());
        Assert.Equal("/manage/authors/link-changes?done=declined", TestBrowser.PathOf(declined.Headers.Location!));
        await fixture.WaitForEmailAsync("wes-withdraw@example.com", "About your C# Advent posts' links");

        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(post.PostUrl, await db.Slots.Where(s => s.Id == post.Id).Select(s => s.PostUrl).SingleAsync(CancellationToken));
        Assert.Equal(
            [PostLinkRequestStatus.Withdrawn, PostLinkRequestStatus.Declined],
            await db.PostLinkRequests.Where(r => r.Contributor.GithubLogin == "wes-withdraw").OrderBy(r => r.Id).Select(r => r.Status).ToListAsync(CancellationToken));
    }

    [Fact]
    public async Task OnlyTheirOwnPostsFromFinishedSeasonsCanBeChanged()
    {
        using var nobody = await fixture.SignInAsync("no-posts-nell", 8803);
        Assert.Contains("You don't have any posts from earlier seasons", WebUtility.HtmlDecode(await nobody.GetPageAsync(Page)));
        Assert.Equal(HttpStatusCode.NotFound, (await nobody.PostFormAsync(Page, "post-links", new() { ["Form.Note"] = "Nothing to change." })).StatusCode);

        using var fay = await fixture.SignInAsync("future-fay-links", 8804);
        var (post, other) = await GiveAsync(16, 18, "Fay Links", 8804);
        int fayId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            fayId = await db.Contributors.Where(c => c.GithubUserId == 8804).Select(c => c.Id).SingleAsync(CancellationToken);
            await db.Slots.Where(s => s.Id == other.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, (int?)null), CancellationToken);
        }

        await TestSeasons.EnsureAsync(fixture, 2129, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Fay Links";
            slot.PostTitle = "Not over yet";
            slot.PostUrl = "https://fay.example.dev/not-over-yet";
            slot.ContributorId = fayId;
        });

        var page = WebUtility.HtmlDecode(await fay.GetPageAsync(Page));
        Assert.Contains(post.PostTitle!, page);
        Assert.DoesNotContain(other.PostTitle!, page);
        Assert.DoesNotContain("Not over yet", page);

        // Someone else's post, swapped into the form, is ignored.
        var tampered = await fay.PostFormAsync(Page, "post-links", new()
        {
            ["Form.Posts[0].SlotId"] = other.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Form.Posts[0].Url"] = "https://evil.example.com",
        });
        Assert.Contains("All of those links are the same as they were.", WebUtility.HtmlDecode(await tampered.Content.ReadAsStringAsync(CancellationToken)));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(other.PostUrl, await db.Slots.Where(s => s.Id == other.Id).Select(s => s.PostUrl).SingleAsync(CancellationToken));
            Assert.False(await db.PostLinkRequests.AnyAsync(r => r.ContributorId == fayId, CancellationToken));
        }
    }

    [Fact]
    public async Task EachPersonCanOnlyAskSoManyTimesAnHour()
    {
        using var rae = await fixture.SignInAsync("rae-links", 8805);
        var (post, _) = await GiveAsync(19, 21, "Rae Links", 8805);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var raeId = await db.Contributors.Where(c => c.GithubUserId == 8805).Select(c => c.Id).SingleAsync(CancellationToken);
            db.PostLinkRequests.AddRange(Enumerable.Range(0, 5).Select(_ => new PostLinkRequest
            {
                ContributorId = raeId,
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Status = PostLinkRequestStatus.Withdrawn,
                DecidedAt = DateTimeOffset.UtcNow,
            }));
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await rae.PostFormAsync(Page, "post-links", LinkFields(await rae.GetPageAsync(Page), new() { [post.Id] = "https://rae.example.dev/post" }));

        Assert.Contains("You've asked a few times in the last hour.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(CancellationToken)));
    }

    [Fact]
    public async Task OrganizersChangeAnAuthorsLinksThemselves()
    {
        using var oli = await fixture.SignInAsync("oli-organized", 8806);
        var (linked, _) = await GiveAsync(22, 23, "Oli Organized", 8806);
        var (unlinked, _) = await RenameAsync(24, 25, "Una Unlinked");
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        int oliId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            oliId = await db.Contributors.Where(c => c.GithubUserId == 8806).Select(c => c.Id).SingleAsync(CancellationToken);
        }

        // A linked author.
        var linksPage = $"/manage/authors/{oliId}/links";
        Assert.Contains($"href=\"{linksPage.TrimStart('/')}\"", await organizer.GetPageAsync($"/manage/authors/{oliId}"));
        var before = await organizer.GetPageAsync(linksPage);
        var saved = await organizer.PostFormAsync(linksPage, "author-links", LinkFields(before, new() { [linked.Id] = "https://oli.example.dev/moved" }));
        Assert.Equal($"{linksPage}?done=saved", TestBrowser.PathOf(saved.Headers.Location!));
        Assert.Equal("https://oli.example.dev/moved", await PostUrlAsync(linked.Id));

        // A page opened before that change is refused rather than overwriting it.
        var stale = LinkFields(before, new() { [linked.Id] = "https://oli.example.dev/again" });
        foreach (var version in VersionInput().Matches(before).Cast<Match>())
        {
            stale[version.Groups["name"].Value] = version.Groups["value"].Value;
        }

        var refused = await organizer.PostFormAsync(linksPage, "author-links", stale);
        Assert.Contains("Someone changed one of these posts since you opened the page.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync(CancellationToken)));
        Assert.Equal("https://oli.example.dev/moved", await PostUrlAsync(linked.Id));

        // An archive author who isn't linked.
        var unlinkedPage = "/manage/authors/unlinked/links?key=una%20unlinked";
        Assert.Contains($"href=\"{unlinkedPage.TrimStart('/')}\"", await organizer.GetPageAsync("/manage/authors/unlinked?key=una%20unlinked"));
        var savedUnlinked = await organizer.PostFormAsync(unlinkedPage, "author-links", LinkFields(await organizer.GetPageAsync(unlinkedPage), new() { [unlinked.Id] = "https://una.example.dev/moved" }));
        Assert.Equal($"{unlinkedPage}&done=saved", TestBrowser.PathOf(savedUnlinked.Headers.Location!));
        Assert.Equal("https://una.example.dev/moved", await PostUrlAsync(unlinked.Id));

        // Only organizers.
        var denied = await oli.GetAsync(linksPage, CancellationToken);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(denied.Headers.Location!));
    }

    /// <summary>
    /// The form's link fields, as a browser posts them: each post's link as the page shows it, or its new link. The page's
    /// hidden inputs (each post's spot and version) are added by <see cref="TestBrowser.PostFormAsync"/>.
    /// </summary>
    private static Dictionary<string, string> LinkFields(string page, Dictionary<int, string> newLinks) =>
        UrlInput().Matches(page).ToDictionary(
            input => $"Form.Posts[{input.Groups["index"].Value}].Url",
            input => newLinks.TryGetValue(int.Parse(TestBrowser.HiddenValue(page, $"Form.Posts[{input.Groups["index"].Value}].SlotId"), System.Globalization.CultureInfo.InvariantCulture), out var link)
                ? link
                : WebUtility.HtmlDecode(ValueAttribute().Match(input.Value).Groups["value"].Value));

    /// <summary>Puts two published 2019 posts between the days (each test has its own) under a name of the test's own, on the contributor's account.</summary>
    private async Task<(Slot First, Slot Second)> GiveAsync(int fromDay, int toDay, string name, long githubUserId)
    {
        var (first, second) = await RenameAsync(fromDay, toDay, name);
        await using var db = await fixture.CreateDbContextAsync();
        var contributorId = await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
        await db.Slots.Where(s => s.Id == first.Id || s.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, contributorId), CancellationToken);
        return (first, second);
    }

    /// <summary>Gives the first two published 2019 posts between the days a name of the test's own, and unlinks them.</summary>
    private async Task<(Slot First, Slot Second)> RenameAsync(int fromDay, int toDay, string name)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var slots = await db.Slots
            .Where(s => s.SeasonYear == 2019 && s.Day >= fromDay && s.Day <= toDay && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Take(2)
            .ToListAsync(CancellationToken);
        foreach (var slot in slots)
        {
            slot.AuthorName = name;
            slot.ContributorId = null;
        }

        await db.SaveChangesAsync(CancellationToken);
        return (slots[0], slots[1]);
    }

    private async Task<int> PendingIdAsync(string login)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.PostLinkRequests
            .Where(r => r.Contributor.GithubLogin == login && r.Status == PostLinkRequestStatus.Pending)
            .Select(r => r.Id)
            .SingleAsync(CancellationToken);
    }

    private async Task<string?> PostUrlAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.Id == slotId).Select(s => s.PostUrl).SingleAsync(CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"Form\\.Posts\\[(?<index>[0-9]+)\\]\\.Url\"[^>]*>")]
    private static partial Regex UrlInput();

    [GeneratedRegex("<input[^>]*name=\"(?<name>Form\\.Posts\\[[0-9]+\\]\\.Version)\"[^>]*value=\"(?<value>[0-9]+)\"")]
    private static partial Regex VersionInput();

    [GeneratedRegex("value=\"(?<value>[^\"]*)\"")]
    private static partial Regex ValueAttribute();
}
