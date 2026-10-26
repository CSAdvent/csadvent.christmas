using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Contributors' own spots in season 2108: adding their post, the spots an organizer added by hand (found by verified
/// email), and dropping out.
/// </summary>
public sealed class MySpotsTests(AppHostFixture fixture)
{
    private const int Year = 2108;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ContributorsSeeTheirSpotsAndPendingRequests()
    {
        using var browser = await fixture.SignInAsync("spots-ada", 6201);
        await GiveSpotAsync(6201, day: 2, part: 1, "Ada Lovelace");
        await RequestAsync(6201, day: 5, part: 2);

        var page = await browser.GetStringAsync("/my-spots", CancellationToken);

        // The spot is C# text, so Razor encodes its middle dot.
        Assert.Contains("December 2, 2108 &#xB7; Post 1", page);
        Assert.Contains("Needs your post's title and URL", page);
        Assert.Contains("Waiting for approval", page);
        Assert.Contains("December 5, 2108 &#xB7; Post 2", page);

        var home = await browser.GetStringAsync("/", CancellationToken);
        Assert.Contains("href=\"my-spots\"", home);
    }

    [Fact]
    public async Task AddingThePostPutsItOnTheCalendar()
    {
        using var browser = await fixture.SignInAsync("spots-bea", 6202);
        var slotId = await GiveSpotAsync(6202, day: 3, part: 1, "Bea");

        // The calendar links the title to the post, so one without the other is refused.
        var incomplete = await browser.PostFormAsync($"/my-spots/{slotId}", "spot", new() { ["Form.AuthorName"] = "Bea", ["Form.PostTitle"] = "Half a post" });
        Assert.Equal(HttpStatusCode.OK, incomplete.StatusCode);
        Assert.Contains("Add both your post", await incomplete.Content.ReadAsStringAsync(CancellationToken));

        var saved = await browser.PostFormAsync($"/my-spots/{slotId}", "spot", new()
        {
            ["Form.AuthorName"] = "Bea Coder",
            ["Form.AuthorUrl"] = "https://example.com/bea",
            ["Form.PostTitle"] = "Pattern matching all the way down",
            ["Form.PostUrl"] = "https://example.com/bea/patterns",
        });

        // A new link is checked for the blurb straight away (#174).
        Assert.Equal($"/my-spots/{slotId}?done=saved&blurb=checked&slot={slotId}", TestBrowser.PathOf(saved.Headers.Location!));
        await using var db = await fixture.CreateDbContextAsync();
        var slot = await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken);
        Assert.Equal(("Bea Coder", "https://example.com/bea", "Pattern matching all the way down", "https://example.com/bea/patterns"),
            (slot.AuthorName, slot.AuthorUrl, slot.PostTitle, slot.PostUrl));
    }

    [Fact]
    public async Task OtherPeoplesSpotsAreNotFound()
    {
        await fixture.SignInAsync("spots-owner", 6203);
        var slotId = await GiveSpotAsync(6203, day: 4, part: 1, "Owner");
        using var stranger = await fixture.SignInAsync("spots-stranger", 6204);

        var response = await stranger.GetAsync($"/my-spots/{slotId}", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SpotsAddedByHandAreFoundByVerifiedEmailAndLinkedOnSave()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        using var browser = await fixture.SignInAsync("spots-grace", 6205);
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 6 && s.Part == 2, CancellationToken);
            slot.AuthorName = "Grace Hopper";
            slot.Contact = new SlotContact { Email = "spots-grace@example.com" };
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
        }

        Assert.Contains($"href=\"my-spots/{slotId}\"", await browser.GetStringAsync("/my-spots", CancellationToken));
        await browser.PostFormAsync($"/my-spots/{slotId}", "spot", new() { ["Form.AuthorName"] = "Grace Hopper" });

        await using var check = await fixture.CreateDbContextAsync();
        Assert.Equal(await ContributorIdAsync(6205), (await check.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).ContributorId);
    }

    [Fact]
    public async Task DroppingOutFreesTheSpotAndTellsTheOrganizers()
    {
        using var browser = await fixture.SignInAsync("spots-dee", 6206);
        var slotId = await GiveSpotAsync(6206, day: 9, part: 2, "Dee Dropout", contactEmail: "spots-dee@example.com");

        var unconfirmed = await browser.PostFormAsync($"/my-spots/{slotId}", "dropout", new());
        Assert.Contains("Tick the box to confirm", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));

        var dropped = await browser.PostFormAsync($"/my-spots/{slotId}", "dropout", new() { ["DropOut.Confirm"] = "true" });

        Assert.Equal($"/my-spots/{slotId}?done=dropped", TestBrowser.PathOf(dropped.Headers.Location!));
        Assert.Contains("You've dropped out", await browser.GetStringAsync(TestBrowser.PathOf(dropped.Headers.Location!), CancellationToken));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.Id == slotId, CancellationToken);
            Assert.Null(slot.AuthorName);
            Assert.Null(slot.ContributorId);
            Assert.Null(slot.Contact);
            Assert.NotNull(slot.DroppedOutAt);
        }

        var notice = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "Dee Dropout dropped out of C# Advent, December 9");
        Assert.Contains("December 9, 2108 (post 2)", notice.Text);
    }

    private async Task<int> GiveSpotAsync(long githubUserId, int day, int part, string author, string? contactEmail = null)
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        var contributorId = await ContributorIdAsync(githubUserId);
        await using var db = await fixture.CreateDbContextAsync();
        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = author;
        slot.ContributorId = contributorId;
        if (contactEmail is not null)
        {
            slot.Contact = new SlotContact { Email = contactEmail };
        }

        await db.SaveChangesAsync(CancellationToken);
        return slot.Id;
    }

    private async Task RequestAsync(long githubUserId, int day, int part)
    {
        var contributorId = await ContributorIdAsync(githubUserId);
        await using var db = await fixture.CreateDbContextAsync();
        var slotId = await db.Slots.Where(s => s.SeasonYear == Year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
        db.ClaimRequests.Add(new ClaimRequest
        {
            SlotId = slotId,
            ContributorId = contributorId,
            DisplayName = "Ada Lovelace",
            Email = "spots-ada@example.com",
            TermsAcceptedAt = DateTimeOffset.UtcNow,
            TimingAcceptedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = ClaimRequestStatus.Pending,
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<int> ContributorIdAsync(long githubUserId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
    }
}
