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
    public async Task ATestPostGoesOutWithoutMarkingAnything()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // Only Facebook's marks are counted: the other test classes mark Bluesky and Mastodon while this runs (#122), and
        // every Facebook mark comes from this class, whose tests run one at a time.
        int before;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            before = await db.SocialPosts.CountAsync(p => p.Platform == SocialPlatform.Facebook, CancellationToken);
        }

        var sent = await PostPlatformFormAsync(organizer, "/manage/social/test", SocialPlatform.Facebook, mode: null);

        Assert.Equal(Page, TestBrowser.PathOf(sent.Headers.Location!));
        Assert.Contains("(only logged: dry run)", await organizer.GetStringAsync(Page, CancellationToken));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(before, await db.SocialPosts.CountAsync(p => p.Platform == SocialPlatform.Facebook, CancellationToken));
        }
    }

    [Fact]
    public async Task EachTestPostHasAPageOfItsOwnForAFreshPreview()
    {
        using var client = fixture.CreateWebClient();

        var page = await client.GetStringAsync("/test-post/abc123def456?via=mastodon", CancellationToken);

        Assert.Contains("<meta property=\"og:title\" content=\"C# Advent test post\" />", page);
        Assert.Contains("<link rel=\"canonical\" href=\"https://localhost:7092/test-post/abc123def456\" />", page);
        Assert.Matches(@"<meta name=""robots"" content=""noindex""\s*/?>", page);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/test-post/Not-An-Id", CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheCredentialAlertIsInTheEmailCatalog()
    {
        using var client = fixture.CreateWebClient();

        var preview = await client.GetStringAsync("/dev/emails/social-credentials-failing", CancellationToken);

        Assert.Contains("Facebook needs new credentials", preview);
    }

    [Fact]
    public async Task TheSignUpsPostGoesOutAutomaticallyOnceOnTheDay()
    {
        // Season 2126's sign-ups opened yesterday.
        await TestSeasons.EnsureAsync(fixture, 2126, claimsOpen: true);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            Assert.Equal(1, await RunSignUpsAsync(2126));
            Assert.Equal(0, await RunSignUpsAsync(2126));

            // LinkedIn is copy and paste, then marked.
            await PostSeasonFormAsync(organizer, "mark", 2126, SeasonPostKind.SignUpsOpen, SocialPlatform.LinkedIn);

            await using (var db = await fixture.CreateDbContextAsync())
            {
                var posts = await db.SeasonSocialPosts.Where(p => p.SeasonYear == 2126).OrderBy(p => p.Platform).ToListAsync(CancellationToken);
                Assert.Equal(
                    [(SocialPlatform.LinkedIn, "organizer"), (SocialPlatform.Facebook, "automation")],
                    posts.Where(p => p.Status == SocialPostStatus.Posted).OrderByDescending(p => p.MarkedBy).Select(p => (p.Platform, p.MarkedBy)));
            }

            var page = await organizer.GetStringAsync("/manage/social/signups?year=2126", CancellationToken);
            Assert.Contains("Sign-ups are open.", page);
            Assert.Contains("Posted automatically", page);
            Assert.Contains("Posted by organizer", page);
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Manual);
        }
    }

    [Fact]
    public async Task TheComingSoonPostCountsDownAndCanGoOutAgain()
    {
        // Season 2127's sign-ups open at midnight Eastern, 8 days from today.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var opens = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date.AddDays(8);
        await TestSeasons.EnsureAsync(fixture, 2127, claimsOpen: false, season => season.ClaimsOpenAt = new DateTimeOffset(opens, eastern.GetUtcOffset(opens)).ToUniversalTime());
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var page = await organizer.GetStringAsync("/manage/social/signups?year=2127", CancellationToken);
        Assert.Contains("C# Advent 2127 sign-ups open in 8 days", page);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Sign-ups open at midnight Eastern on {opens:MMMM d}, in 8 days."), page);

        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            await PostSeasonFormAsync(organizer, "post", 2127, SeasonPostKind.SignUpsSoon, SocialPlatform.Facebook);
            await PostSeasonFormAsync(organizer, "post", 2127, SeasonPostKind.SignUpsSoon, SocialPlatform.Facebook);

            // The "open" post can't go out before sign-ups open.
            var early = await PostSeasonFormAsync(organizer, "post", 2127, SeasonPostKind.SignUpsOpen, SocialPlatform.Facebook);
            Assert.EndsWith("&error=not-posted", TestBrowser.PathOf(early.Headers.Location!));

            await using var db = await fixture.CreateDbContextAsync();
            Assert.Equal(2, await db.SeasonSocialPosts.CountAsync(p => p.SeasonYear == 2127 && p.Kind == SeasonPostKind.SignUpsSoon && p.Status == SocialPostStatus.Posted, CancellationToken));
            Assert.False(await db.SeasonSocialPosts.AnyAsync(p => p.SeasonYear == 2127 && p.Kind == SeasonPostKind.SignUpsOpen, CancellationToken));
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Manual);
        }
    }

    [Fact]
    public async Task TheSignUpsPageSaysWhenSignUpsOpenWhateverTheTime()
    {
        // Season 2300's sign-ups open at 9 AM Eastern, 8 days from today.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var opens = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date.AddDays(8).AddHours(9);
        await TestSeasons.EnsureAsync(fixture, 2300, claimsOpen: false, season => season.ClaimsOpenAt = new DateTimeOffset(opens, eastern.GetUtcOffset(opens)).ToUniversalTime());
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var page = await organizer.GetStringAsync("/manage/social/signups?year=2300", CancellationToken);

        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Sign-ups open at 9 AM Eastern on {opens:MMMM d}, in 8 days."), page);
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

    /// <summary>Runs the "sign-ups are open" posts at noon Eastern today, after the morning send time.</summary>
    private async Task<int> RunSignUpsAsync(int year)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date;
        var noon = new DateTimeOffset(today.AddHours(12), eastern.GetUtcOffset(today.AddHours(12))).ToUniversalTime();
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/social/signups/run?at={Uri.EscapeDataString(noon.ToString("O", CultureInfo.InvariantCulture))}&year={year}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignUpsRun>(CancellationToken))!.Posted;
    }

    private async Task<HttpResponseMessage> PostSeasonFormAsync(HttpClient organizer, string action, int year, SeasonPostKind kind, SocialPlatform platform)
    {
        var html = await organizer.GetStringAsync($"/manage/social/signups?year={year}", CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Year"] = year.ToString(CultureInfo.InvariantCulture),
            ["Kind"] = kind.ToString(),
            ["Platform"] = platform.ToString(),
        };
        return await organizer.PostAsync($"/manage/social/signups/{action}", new FormUrlEncodedContent(fields), CancellationToken);
    }

    private Task<HttpResponseMessage> SetModeAsync(HttpClient organizer, SocialPlatform platform, SocialPostingMode mode) =>
        PostPlatformFormAsync(organizer, "/manage/social/mode", platform, mode);

    private async Task<HttpResponseMessage> PostPlatformFormAsync(HttpClient organizer, string action, SocialPlatform platform, SocialPostingMode? mode)
    {
        var html = await organizer.GetStringAsync(Page, CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Platform"] = platform.ToString(),
            ["ReturnUrl"] = Page.TrimStart('/'),
        };
        if (mode is { } value)
        {
            fields["Mode"] = value.ToString();
        }

        return await organizer.PostAsync(action, new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed record RunResult(int Posted, int Failed);

    private sealed record SignUpsRun(int Posted);
}
