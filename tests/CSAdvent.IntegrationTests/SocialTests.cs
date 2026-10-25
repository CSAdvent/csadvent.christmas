using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>The organizers' copy-and-paste announcements page, using the seeded history (revealed) and a future test season.</summary>
public sealed partial class SocialTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachRevealedEntryHasAnAnnouncementForEveryPlatform()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var page = await organizer.GetStringAsync("/manage/social?year=2024&day=3", CancellationToken);

        foreach (var platform in Enum.GetValues<SocialPlatform>())
        {
            Assert.Contains($"https://localhost:7092/2024/day/3/1?via={platform.ToString().ToLowerInvariant()}", page);
        }

        Assert.Contains("data-copy=\"announcement-", page);
        Assert.Contains("https://bsky.app/intent/compose?text=", page);
        Assert.Contains("https://mastodon.social/share?text=", page);
        Assert.Contains("https://www.reddit.com/r/csadvent/submit?url=", page);
        Assert.Contains("https://x.com/intent/tweet?text=", page);
        Assert.DoesNotContain("Preview only", page);
    }

    [Fact]
    public async Task MarkingAPostRecordsWhoAndWhereAndCanBeUndone()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == 2024 && s.Day == 4 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        var marked = await PostAsync(organizer, "/manage/social?year=2024&day=4", "mark", slotId, SocialPlatform.Bluesky, "https://bsky.app/profile/csadvent/post/abc");

        Assert.Equal("/manage/social?year=2024&day=4", TestBrowser.PathOf(marked.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var post = await db.SocialPosts.SingleAsync(p => p.SlotId == slotId && p.Platform == SocialPlatform.Bluesky, CancellationToken);
            Assert.Equal((SocialPostStatus.Posted, "organizer", "https://bsky.app/profile/csadvent/post/abc"), (post.Status, post.MarkedBy, post.PostUrl));
        }

        Assert.Contains("Posted by organizer", await organizer.GetStringAsync("/manage/social?year=2024&day=4", CancellationToken));

        await PostAsync(organizer, "/manage/social?year=2024&day=4", "unmark", slotId, SocialPlatform.Bluesky);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.False(await db.SocialPosts.AnyAsync(p => p.SlotId == slotId, CancellationToken));
        }
    }

    [Fact]
    public async Task UpcomingDaysCanBePreviewedButNotPosted()
    {
        await TestSeasons.EnsureAsync(fixture, 2115, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 6, Part: 2 });
            slot.AuthorName = "Early Ed";
            slot.PostTitle = "A 2115 post";
            slot.PostUrl = "https://example.com/2115";
        });
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == 2115 && s.Day == 6 && s.Part == 2).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        var page = await organizer.GetStringAsync("/manage/social?year=2115&day=6", CancellationToken);
        Assert.Contains("Preview only", page);
        Assert.Contains("A 2115 post", page);
        Assert.DoesNotContain("Open composer", page);

        var refused = await PostAsync(organizer, "/manage/social?year=2115&day=6", "mark", slotId, SocialPlatform.Mastodon);
        Assert.EndsWith("error=not-revealed", refused.Headers.Location!.OriginalString);
        await using var check = await fixture.CreateDbContextAsync();
        Assert.False(await check.SocialPosts.AnyAsync(p => p.SlotId == slotId, CancellationToken));

        // A day without posts has nothing to announce.
        Assert.Contains("No posts for this day yet", await organizer.GetStringAsync("/manage/social?year=2115&day=7", CancellationToken));
    }

    [Fact]
    public async Task OnlyOrganizersCanSeeOrMarkAnnouncements()
    {
        using var contributor = await fixture.SignInAsync("social-nosy", 6401);

        var response = await contributor.GetAsync("/manage/social", CancellationToken);

        Assert.StartsWith("/access-denied", TestBrowser.PathOf(response.Headers.Location!));
    }

    /// <summary>Posts one of the page's plain mark/unmark forms, with the page's antiforgery token.</summary>
    private async Task<HttpResponseMessage> PostAsync(HttpClient browser, string page, string action, int slotId, SocialPlatform platform, string? postUrl = null)
    {
        var html = await browser.GetStringAsync(page, CancellationToken);
        var token = AntiforgeryToken().Match(html).Groups["token"].Value;
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token),
            ["SlotId"] = $"{slotId}",
            ["Platform"] = platform.ToString(),
            ["ReturnUrl"] = page.TrimStart('/'),
        };
        if (postUrl is not null)
        {
            fields["PostUrl"] = postUrl;
        }

        return await browser.PostAsync($"/manage/social/{action}", new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
