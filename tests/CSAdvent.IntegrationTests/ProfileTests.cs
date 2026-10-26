using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Contributors manage how they're credited on their profile. Uses season 2124, and one 2025 archive post.</summary>
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
        Assert.Contains("Fill in from GitHub</button>", page);

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
    public async Task EntryPagesLinkTheAuthorsGitHubAndAccounts()
    {
        using var author = await fixture.SignInAsync("links-lee", 8406);
        var refused = await author.PostFormAsync("/profile", "profile", new() { ["Form.Name"] = "Lee Links", ["Form.Link"] = "https://hachyderm.io/@lee" });
        Assert.Contains("Add it under Social accounts", await refused.Content.ReadAsStringAsync(CancellationToken));
        await author.PostFormAsync("/profile", "profile", new() { ["Form.Name"] = "Lee Links", ["Form.Bluesky"] = "lee.bsky.social" });

        // A published post of theirs with no website, so their name links to GitHub.
        int day, part;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Where(s => s.SeasonYear == 2025 && s.Day == 12 && s.PostTitle != null && s.PostUrl != null).OrderBy(s => s.Part).FirstAsync(CancellationToken);
            slot.AuthorName = "Lee Links";
            slot.AuthorUrl = null;
            slot.ContributorId = await db.Contributors.Where(c => c.GithubUserId == 8406).Select(c => c.Id).SingleAsync(CancellationToken);
            await db.SaveChangesAsync(CancellationToken);
            (day, part) = (slot.Day, slot.Part);
        }

        using var reader = fixture.CreateBrowser();
        using var preview = fixture.CreateLinkPreview();
        var page = await preview.GetStringAsync($"/2025/day/{day}/{part}", CancellationToken);
        Assert.Contains("<a href=\"https://github.com/links-lee\" class=\"font-medium text-advent-700 underline\">Lee Links</a>", page);
        Assert.Contains("<a href=\"https://bsky.app/profile/lee.bsky.social\" class=\"text-advent-700 underline\">Bluesky</a>", page);
        Assert.Contains("href=\"https://github.com/links-lee\"", await reader.GetStringAsync("/archive/2025", CancellationToken));
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
