using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Contributors manage how they're credited on their profile. Uses season 2124.</summary>
public sealed class ProfileTests(AppHostFixture fixture)
{
    private const int Year = 2124;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProfileFillsInTheNextClaimAndCanRenameEverySpot()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        using var browser = await fixture.SignInAsync("profile-pat", 8404, "Pat From GitHub");

        // Before they've saved anything, it starts from their GitHub profile name.
        var page = await browser.GetStringAsync("/profile", CancellationToken);
        Assert.Contains("value=\"Pat From GitHub\"", page);
        Assert.Contains("profile-pat@example.com", page);

        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 3 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Pat Old";
            slot.ContributorId = await db.Contributors.Where(c => c.GithubUserId == 8404).Select(c => c.Id).SingleAsync(CancellationToken);
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
        }

        var invalid = await browser.PostFormAsync("/profile", "profile", new() { ["Form.Name"] = "Pat", ["Form.Link"] = "javascript:alert(1)" });
        Assert.Contains("Enter a full web address", await invalid.Content.ReadAsStringAsync(CancellationToken));

        var saved = await browser.PostFormAsync("/profile", "profile", new()
        {
            ["Form.Name"] = "Pat Profile",
            ["Form.Link"] = "https://example.com/pat",
            ["Form.Bluesky"] = "@pat.bsky.social",
            ["Form.UpdatePosts"] = "true",
        });
        Assert.Equal("/profile?done=saved", TestBrowser.PathOf(saved.Headers.Location!));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributor = await db.Contributors.AsNoTracking().SingleAsync(c => c.GithubUserId == 8404, CancellationToken);
            Assert.Equal(("Pat Profile", "https://example.com/pat", "pat.bsky.social"), (contributor.DisplayName, contributor.WebsiteUrl, contributor.BlueskyHandle));
            var slot = await db.Slots.AsNoTracking().SingleAsync(s => s.Id == slotId, CancellationToken);
            Assert.Equal(("Pat Profile", "https://example.com/pat"), (slot.AuthorName, slot.AuthorUrl));
        }

        var claim = await browser.GetStringAsync($"/claim/{Year}/4/1", CancellationToken);
        Assert.Contains("value=\"Pat Profile\"", claim);
        Assert.Contains("value=\"https://example.com/pat\"", claim);
        Assert.Contains("value=\"pat.bsky.social\"", claim);
    }

    [Fact]
    public async Task AClaimUpdatesTheProfile()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        using var browser = await fixture.SignInAsync("profile-quin", 8405);

        await browser.PostFormAsync($"/claim/{Year}/6/2", "claim", new()
        {
            ["Form.DisplayName"] = "Quin Claims",
            ["Form.Link"] = "https://example.com/quin",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });

        var page = await browser.GetStringAsync("/profile", CancellationToken);
        Assert.Contains("value=\"Quin Claims\"", page);
        Assert.Contains("value=\"https://example.com/quin\"", page);
    }

    [Fact]
    public async Task TheProfileNeedsSigningIn()
    {
        using var anonymous = fixture.CreateBrowser();

        var response = await anonymous.GetAsync("/profile", CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?ReturnUrl=%2Fprofile", response.Headers.Location!.PathAndQuery);
    }
}
