using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Automatic posting (#61), in dry-run mode: the tests never reach a real account. Uses Facebook (SocialTests marks Bluesky
/// and Mastodon by hand) and a December 2025 day no other test reads.
/// </summary>
public sealed partial class SocialPostingTests(AppHostFixture fixture)
{
    private const string Page = "/manage/social?year=2025&day=8";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AutomaticPlatformsPostEachEntryOnceAtItsTime()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        int[] slots;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slots = await db.Slots.Where(s => s.SeasonYear == 2025 && s.Day == 8 && s.PostTitle != null && s.PostUrl != null && s.AuthorName != null)
                .OrderBy(s => s.Part).Select(s => s.Id).ToArrayAsync(CancellationToken);
        }

        Assert.Equal(2, slots.Length);
        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            Assert.Contains("Dry run: automatic posts are only logged.", await organizer.GetStringAsync(Page, CancellationToken));

            // Before 10 AM nothing is due. At 10:30 post 1 goes out; post 2 waits for 2 PM.
            Assert.Equal(0, (await RunAsync(new DateTime(2025, 12, 8, 9, 30, 0))).Posted);
            Assert.Equal(1, (await RunAsync(new DateTime(2025, 12, 8, 10, 30, 0))).Posted);
            Assert.Equal([slots[0]], await PostedAsync(slots));

            // At 2:30 post 2 goes out, and post 1 isn't posted again.
            Assert.Equal(1, (await RunAsync(new DateTime(2025, 12, 8, 14, 30, 0))).Posted);
            Assert.Equal(0, (await RunAsync(new DateTime(2025, 12, 8, 14, 35, 0))).Posted);
            Assert.Equal(slots, await PostedAsync(slots));

            Assert.Contains("Posted automatically", await organizer.GetStringAsync(Page, CancellationToken));
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Manual);
        }
    }

    [Fact]
    public async Task OnlyPlatformsThatCanPostAutomaticallyCanBeAutomatic()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var refused = await SetModeAsync(organizer, SocialPlatform.LinkedIn, SocialPostingMode.Automatic);

        Assert.Equal($"{Page}&error=no-credentials", TestBrowser.PathOf(refused.Headers.Location!));
        Assert.Contains("Copy and paste only: there's no API access.", await organizer.GetStringAsync(Page, CancellationToken));
    }

    [Fact]
    public async Task OffPlatformsAreLeftOffTheSocialPage()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await SetModeAsync(organizer, SocialPlatform.Reddit, SocialPostingMode.Off);
        try
        {
            Assert.DoesNotContain("https://www.reddit.com/r/csadvent/submit?url=", await organizer.GetStringAsync(Page, CancellationToken));
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Reddit, SocialPostingMode.Manual);
        }

        Assert.Contains("https://www.reddit.com/r/csadvent/submit?url=", await organizer.GetStringAsync(Page, CancellationToken));
    }

    /// <summary>Runs automatic posting at a time in December 2025, Eastern (UTC-5).</summary>
    private async Task<RunResult> RunAsync(DateTime eastern)
    {
        var at = new DateTimeOffset(eastern, TimeSpan.FromHours(-5)).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/social/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResult>(CancellationToken))!;
    }

    private async Task<int[]> PostedAsync(int[] slots)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.SocialPosts
            .Where(p => slots.Contains(p.SlotId) && p.Platform == SocialPlatform.Facebook && p.Status == SocialPostStatus.Posted && p.MarkedBy == "automation")
            .OrderBy(p => p.SlotId)
            .Select(p => p.SlotId)
            .ToArrayAsync(CancellationToken);
    }

    private async Task<HttpResponseMessage> SetModeAsync(HttpClient organizer, SocialPlatform platform, SocialPostingMode mode)
    {
        var html = await organizer.GetStringAsync(Page, CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Platform"] = platform.ToString(),
            ["Mode"] = mode.ToString(),
            ["ReturnUrl"] = Page.TrimStart('/'),
        };
        return await organizer.PostAsync("/manage/social/mode", new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed record RunResult(int Posted, int Failed);
}
