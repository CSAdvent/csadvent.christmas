using System.Net;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Contributors ask for older posts to be added to their account, and organizers approve or decline (#120). Renames a few
/// late-December 2022 archive posts to names of its own (no other test reads those names).
/// </summary>
public sealed class PostClaimTests(AppHostFixture fixture)
{
    private const string Organizers = "hello@csadvent.christmas";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ContributorsAskForTheirOlderPostsAndOrganizersApprove()
    {
        var (first, second) = await RenameAsync(18, 20, "Pat Claimant", "Pat Claimant");
        using var pat = await fixture.SignInAsync("pat-claimant", 8601);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        using var reader = fixture.CreateWebClient();
        var claimPage = $"/my-spots/claim/2022/{first.Day}/{first.Part}";

        Assert.Contains($"href=\"{claimPage.TrimStart('/')}\"", await reader.GetPageAsync($"/2022/day/{first.Day}/{first.Part}"));

        // Their name finds both posts.
        var found = WebUtility.HtmlDecode(await pat.GetPageAsync("/my-spots?find=pat+claimant"));
        Assert.Contains(first.PostTitle!, found);
        Assert.Contains(second.PostTitle!, found);
        Assert.Contains($"href=\"{claimPage.TrimStart('/')}\"", found);

        var unconfirmed = await pat.PostFormAsync(claimPage, "post-claim", new() { ["Form.Note"] = "From my old blog." });
        Assert.Contains("Tick the box to confirm you wrote this post.", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));

        var requested = await pat.PostFormAsync(claimPage, "post-claim", new() { ["Form.Note"] = "From my old blog.", ["Form.IWroteIt"] = "true" });
        Assert.Equal($"{claimPage}?done=requested", TestBrowser.PathOf(requested.Headers.Location!));
        Assert.Contains("You've asked for this one.", WebUtility.HtmlDecode(await pat.GetPageAsync(claimPage)));
        Assert.Contains("Waiting for an organizer", await pat.GetPageAsync("/my-spots"));

        var notice = await fixture.WaitForEmailAsync(Organizers, "Is this their post? pat-claimant asked for a C# Advent 2022 post");
        Assert.NotNull(notice);

        var requestId = await RequestIdAsync(first.Id, "pat-claimant");
        Assert.Contains("manage/authors/requests", await organizer.GetPageAsync("/manage/authors"));
        Assert.Contains(first.PostTitle!, WebUtility.HtmlDecode(await organizer.GetPageAsync("/manage/authors/requests")));

        var review = $"/manage/authors/requests/{requestId}";
        var reviewPage = WebUtility.HtmlDecode(await organizer.GetPageAsync(review));
        Assert.Contains("From my old blog.", reviewPage);
        Assert.Contains("Also add their other post as “Pat Claimant”", reviewPage);

        var approved = await organizer.PostFormAsync(review, "approve", new() { ["Approve.AlsoSameName"] = "true" });
        Assert.Equal("/manage/authors/requests?done=approved", TestBrowser.PathOf(approved.Headers.Location!));
        await fixture.WaitForEmailAsync("pat-claimant@example.com", "Your C# Advent 2022 post is on your account");

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var patId = await db.Contributors.Where(c => c.GithubUserId == 8601).Select(c => c.Id).SingleAsync(CancellationToken);
            Assert.Equal(patId, await db.Slots.Where(s => s.Id == first.Id).Select(s => s.ContributorId).SingleAsync(CancellationToken));
            Assert.Equal(patId, await db.Slots.Where(s => s.Id == second.Id).Select(s => s.ContributorId).SingleAsync(CancellationToken));
            var request = await db.PostClaimRequests.SingleAsync(r => r.Id == requestId, CancellationToken);
            Assert.Equal(PostClaimStatus.Approved, request.Status);
            Assert.Equal("organizer", request.DecidedBy);
        }

        Assert.Contains("Your C# Advent history", await pat.GetPageAsync("/my-spots"));
        Assert.Contains("It's already on your account", WebUtility.HtmlDecode(await pat.GetPageAsync(claimPage)));
        Assert.DoesNotContain("Add it to your account", await reader.GetPageAsync($"/2022/day/{first.Day}/{first.Part}"));
    }

    [Fact]
    public async Task OrganizersDeclineAndPostsOnSomeoneElsesAccountCantBeRequested()
    {
        var (declined, taken) = await RenameAsync(21, 23, "Quinn Decline", "Someone Else");
        using var quinn = await fixture.SignInAsync("quinn-decline", 8602);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var claimPage = $"/my-spots/claim/2022/{declined.Day}/{declined.Part}";

        await quinn.PostFormAsync(claimPage, "post-claim", new() { ["Form.IWroteIt"] = "true" });
        var review = $"/manage/authors/requests/{await RequestIdAsync(declined.Id, "quinn-decline")}";
        var declinedResponse = await organizer.PostFormAsync(review, "decline", new());
        Assert.Equal("/manage/authors/requests?done=declined", TestBrowser.PathOf(declinedResponse.Headers.Location!));
        await fixture.WaitForEmailAsync("quinn-decline@example.com", "About your C# Advent 2022 post");

        // They can ask again; meanwhile someone else gets the post, so it can't be approved.
        await quinn.PostFormAsync(claimPage, "post-claim", new() { ["Form.IWroteIt"] = "true" });
        var again = $"/manage/authors/requests/{await RequestIdAsync(declined.Id, "quinn-decline")}";
        int otherId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var other = new Contributor { GithubUserId = 8603, GithubLogin = "post-owner", CreatedAt = DateTimeOffset.UtcNow };
            db.Contributors.Add(other);
            await db.SaveChangesAsync(CancellationToken);
            otherId = other.Id;
            await db.Slots.Where(s => s.Id == declined.Id || s.Id == taken.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, otherId), CancellationToken);
        }

        Assert.Contains("is on post-owner's account now", WebUtility.HtmlDecode(await organizer.GetPageAsync(again)));
        var refused = await organizer.PostFormAsync(again, "approve", new());
        Assert.Contains("on someone else", await refused.Content.ReadAsStringAsync(CancellationToken));

        // A post on someone else's account says who to ask instead.
        var takenPage = WebUtility.HtmlDecode(await quinn.GetPageAsync($"/my-spots/claim/2022/{taken.Day}/{taken.Part}"));
        Assert.Contains("This post is already on someone's account.", takenPage);
        Assert.Contains("mailto:hello@csadvent.christmas", takenPage);
        Assert.DoesNotContain("I wrote this post", takenPage);
        Assert.Contains("mailto:hello@csadvent.christmas", await quinn.GetPageAsync("/my-spots"));
    }

    [Fact]
    public async Task EachPersonCanOnlyAskForSoManyPostsAnHour()
    {
        var (post, _) = await RenameAsync(24, 25, "Rae Limit", "Rae Limit");
        using var rae = await fixture.SignInAsync("rae-limit", 8604);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var raeId = await db.Contributors.Where(c => c.GithubUserId == 8604).Select(c => c.Id).SingleAsync(CancellationToken);
            var slots = await db.Slots.Where(s => s.SeasonYear == 2021 && s.PostUrl != null).Select(s => s.Id).Take(10).ToListAsync(CancellationToken);
            db.PostClaimRequests.AddRange(slots.Select(slotId => new PostClaimRequest
            {
                SlotId = slotId,
                ContributorId = raeId,
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Status = PostClaimStatus.Declined,
                DecidedAt = DateTimeOffset.UtcNow,
                DecidedBy = "organizer",
            }));
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await rae.PostFormAsync($"/my-spots/claim/2022/{post.Day}/{post.Part}", "post-claim", new() { ["Form.IWroteIt"] = "true" });

        Assert.Contains("You've asked for a lot of posts in the last hour.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(CancellationToken)));
    }

    [Fact]
    public async Task OnlyFinishedSeasonsPostsCanBeRequested()
    {
        await TestSeasons.EnsureAsync(fixture, 2128, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Future Fay";
            slot.PostTitle = "Not over yet";
            slot.PostUrl = "https://example.com/future";
        });
        using var fay = await fixture.SignInAsync("future-fay", 8605);

        Assert.Equal(HttpStatusCode.NotFound, (await fay.GetAsync("/my-spots/claim/2128/1/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fay.GetAsync("/my-spots/claim/9000/1/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fay.GetAsync("/my-spots/claim/2022/26/1", CancellationToken)).StatusCode);
    }

    /// <summary>Gives the first two 2022 posts between the days (each test has its own) names of the test's own, and unlinks them.</summary>
    private async Task<(Slot First, Slot Second)> RenameAsync(int fromDay, int toDay, string firstName, string secondName)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var slots = await db.Slots
            .Where(s => s.SeasonYear == 2022 && s.Day >= fromDay && s.Day <= toDay && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Take(2)
            .ToListAsync(CancellationToken);
        slots[0].AuthorName = firstName;
        slots[1].AuthorName = secondName;
        slots[0].ContributorId = null;
        slots[1].ContributorId = null;
        await db.SaveChangesAsync(CancellationToken);
        return (slots[0], slots[1]);
    }

    private async Task<int> RequestIdAsync(int slotId, string login)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.PostClaimRequests
            .Where(r => r.SlotId == slotId && r.Contributor.GithubLogin == login && r.Status == PostClaimStatus.Pending)
            .Select(r => r.Id)
            .SingleAsync(CancellationToken);
    }
}
