using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Automatic posting (#61), in dry-run mode: the tests never reach a real account. Uses Facebook (SocialTests marks Bluesky
/// and Mastodon by hand) and a December 2025 day no other test reads, and seasons 2640 and 2641 for runs at a time the real
/// clock hasn't reached (#184).
/// </summary>
public sealed partial class SocialPostingTests(AppHostFixture fixture)
{
    private const string Page = "/manage/social?year=2025&day=8";

    /// <summary>
    /// When the seasons whose sign-ups are already open opened: 9 AM Eastern (daylight time) on October 1, 2026, in the past
    /// for the pages, which go by the real clock. The automatic posts run at a fixed time after it, never "today" (#198).
    /// </summary>
    private static readonly DateTimeOffset SignUpsOpenedAt = new(2026, 10, 1, 13, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo EasternTime = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

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
    public async Task ARunPostsAtTheTimeItsGivenNotTheRealClock()
    {
        // Season 2640's December 3, post 1 has passed its blurb check. By the real clock its reveal is centuries away, but the
        // run says it's that morning, and everything it decides follows the run's time (#184).
        await TestSeasons.EnsureAsync(fixture, 2640, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 3, Part: 1 });
            slot.AuthorName = "Grace Hopper";
            slot.PostTitle = "Compilers for the holidays";
            slot.PostUrl = "https://example.com/compilers-2640";
        });
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == 2640 && s.Day == 3 && s.Part == 1, CancellationToken);
            slotId = slot.Id;
            db.BlurbChecks.Add(new BlurbCheck { SlotId = slotId, PostUrl = slot.PostUrl!, Status = BlurbStatus.Passed, StatusSince = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            Assert.Equal(0, (await RunAtAsync(Eastern(new DateTime(2640, 12, 3, 9, 30, 0)))).Posted);
            Assert.Equal(1, (await RunAtAsync(Eastern(new DateTime(2640, 12, 3, 10, 30, 0)))).Posted);
            Assert.Equal([slotId], await PostedAsync([slotId]));
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Manual);
        }
    }

    [Fact]
    public async Task TheSignUpsPostGoesOutAtTheTimeTheRunIsGiven()
    {
        // Season 2641's sign-ups open at midnight Eastern on November 1, 2641: centuries away by the real clock, but that
        // morning by the run's (#184).
        await TestSeasons.EnsureAsync(fixture, 2641, claimsOpen: false, season => season.ClaimsOpenAt = Eastern(new DateTime(2641, 11, 1)));
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            Assert.Equal(0, await RunSignUpsAtAsync(2641, Eastern(new DateTime(2641, 11, 1, 8, 0, 0))));
            Assert.Equal(1, await RunSignUpsAtAsync(2641, Eastern(new DateTime(2641, 11, 1, 10, 0, 0))));
            Assert.Equal(0, await RunSignUpsAtAsync(2641, Eastern(new DateTime(2641, 11, 1, 10, 5, 0))));

            await using var db = await fixture.CreateDbContextAsync();
            var post = await db.SeasonSocialPosts.SingleAsync(p => p.SeasonYear == 2641, CancellationToken);
            Assert.Equal((SeasonPostKind.SignUpsOpen, SocialPostStatus.Posted), (post.Kind, post.Status));
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
        var startedAt = DateTimeOffset.UtcNow;

        var sent = await PostPlatformFormAsync(organizer, "/manage/social/test", SocialPlatform.Facebook, mode: null);

        var finishedAt = DateTimeOffset.UtcNow;
        Assert.Equal(Page, TestBrowser.PathOf(sent.Headers.Location!));
        Assert.Contains("(only logged: dry run)", await organizer.GetStringAsync(Page, CancellationToken));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            // It's recorded as the platform's test post, and no post was marked while it went out. Only those moments count,
            // not every Facebook post there is, which other test classes can add to while this runs (#122, #198).
            var setting = await db.SocialPlatformSettings.AsNoTracking().SingleAsync(s => s.Platform == SocialPlatform.Facebook, CancellationToken);
            Assert.InRange(setting.TestedAt!.Value, startedAt, finishedAt);
            Assert.False(await db.SocialPosts.AnyAsync(p => p.Platform == SocialPlatform.Facebook && p.At >= startedAt && p.At <= finishedAt, CancellationToken));
        }
    }

    [Fact]
    public async Task AnOrganizerRetriesAFailedPostAndItGoesOut()
    {
        // December 11, 2025's post 1 failed on Facebook three times, so automatic posting has stopped trying (#199).
        const string page = "/manage/social?year=2025&day=11";
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == 2025 && s.Day == 11 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
            db.SocialPosts.Add(new SocialPost
            {
                SlotId = slotId,
                Platform = SocialPlatform.Facebook,
                Status = SocialPostStatus.Failed,
                At = DateTimeOffset.UtcNow.AddHours(-2),
                MarkedBy = "automation",
                Attempts = 3,
                Error = "Facebook answered 500 (Internal Server Error)",
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        Assert.Contains("Automatic posting failed (3 tries): Facebook answered 500 (Internal Server Error)", System.Net.WebUtility.HtmlDecode(await organizer.GetPageAsync(page)));

        // Someone who isn't an organizer can't retry it.
        using (var nosy = await fixture.SignInAsync("social-retry-nosy", 262501))
        {
            var refused = await PostSlotFormAsync(nosy, "/announcements", "/manage/social/retry", slotId, page);
            Assert.StartsWith("/access-denied", TestBrowser.PathOf(refused.Headers.Location!));
            Assert.Equal(SocialPostStatus.Failed, (await FacebookPostAsync(slotId)).Status);
        }

        // Retry now posts it (dry run), as a fresh first try, and comes back to the page without an error.
        var retried = await PostSlotFormAsync(organizer, page, "/manage/social/retry", slotId, page);
        Assert.Equal(page, TestBrowser.PathOf(retried.Headers.Location!));
        var post = await FacebookPostAsync(slotId);
        Assert.Equal((SocialPostStatus.Posted, 1, "automation", (string?)null), (post.Status, post.Attempts, post.MarkedBy, post.Error));
        Assert.Contains("Posted automatically", await organizer.GetPageAsync(page));

        // Pressing it again, once it's posted, posts nothing more.
        await PostSlotFormAsync(organizer, page, "/manage/social/retry", slotId, page);
        Assert.Equal(post.At, (await FacebookPostAsync(slotId)).At);
    }

    [Fact]
    public async Task CheckingAPlatformsCredentialsRecordsTheResult()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var startedAt = DateTimeOffset.UtcNow;

        // The dry run's stand-in accepts any check (#199).
        var checkedFacebook = await PostPlatformFormAsync(organizer, "/manage/social/check", SocialPlatform.Facebook, mode: null);

        Assert.Equal(Page, TestBrowser.PathOf(checkedFacebook.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var setting = await db.SocialPlatformSettings.AsNoTracking().SingleAsync(s => s.Platform == SocialPlatform.Facebook, CancellationToken);
            Assert.True(setting.CheckedAt >= startedAt, $"It was last checked at {setting.CheckedAt:O}, before this test.");
            Assert.Null(setting.CheckError);
        }

        Assert.Contains("they work.", await organizer.GetPageAsync(Page));

        // Without an organizer's antiforgery token, nothing's checked.
        var forged = await organizer.PostAsync("/manage/social/check", new FormUrlEncodedContent([new("Platform", "Facebook"), new("ReturnUrl", Page.TrimStart('/'))]), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
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
        await TestSeasons.EnsureAsync(fixture, 2126, claimsOpen: false, season => season.ClaimsOpenAt = SignUpsOpenedAt);
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
        var opens = TodayInEastern().AddDays(8);
        await TestSeasons.EnsureAsync(fixture, 2127, claimsOpen: false, season => season.ClaimsOpenAt = Eastern(opens));
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var page = await organizer.GetStringAsync("/manage/social/signups?year=2127", CancellationToken);
        AssertContainsDaysUntil(page, opens, days => $"C# Advent 2127 sign-ups open in {days} days");
        AssertContainsDaysUntil(page, opens, days => string.Create(CultureInfo.InvariantCulture, $"Sign-ups open at midnight Eastern on {opens:MMMM d}, in {days} days."));

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
        var opens = TodayInEastern().AddDays(8).AddHours(9);
        await TestSeasons.EnsureAsync(fixture, 2300, claimsOpen: false, season => season.ClaimsOpenAt = Eastern(opens));
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var page = await organizer.GetStringAsync("/manage/social/signups?year=2300", CancellationToken);

        AssertContainsDaysUntil(page, opens, days => string.Create(CultureInfo.InvariantCulture, $"Sign-ups open at 9 AM Eastern on {opens:MMMM d}, in {days} days."));
    }

    [Fact]
    public async Task AnInterruptedAnnouncementWaitsForAnOrganizerToCheck()
    {
        // Season 2520's December 4 post 1 was being posted to Facebook when the app stopped, 20 minutes ago.
        await TestSeasons.EnsureAsync(fixture, 2520, claimsOpen: false);
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == 2520 && s.Day == 4 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Ada Lovelace";
            slot.PostTitle = "Interrupted on the way out";
            slot.PostUrl = "https://example.com/interrupted";
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
            db.SocialPosts.Add(new SocialPost
            {
                SlotId = slotId,
                Platform = SocialPlatform.Facebook,
                Status = SocialPostStatus.Posting,
                At = DateTimeOffset.UtcNow.AddMinutes(-20),
                MarkedBy = "automation",
                Attempts = 1,
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        // It's found by the real clock, whatever time the run says it is, and with no platform in Automatic mode.
        await RunAsync(new DateTime(2025, 12, 8, 9, 0, 0));
        await RunAsync(new DateTime(2025, 12, 8, 9, 5, 0));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var post = await db.SocialPosts.SingleAsync(p => p.SlotId == slotId && p.Platform == SocialPlatform.Facebook, CancellationToken);
            Assert.Equal((SocialPostStatus.Unconfirmed, 1), (post.Status, post.Attempts));
            Assert.StartsWith("Interrupted while posting", post.Error);
        }

        // The organizers hear about it once: each run has queued what it sends by the time it returns. (Counting what MailPit
        // got would wait for the organizers' queue to empty, which other tests keep filling.)
        const string Subject = "Did C# Advent 2520, December 4, post 1 go out on Facebook?";
        var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", Subject);
        Assert.Contains("C# Advent 2520, December 4, post 1 (“Interrupted on the way out”)", email.Text);
        Assert.Contains("https://localhost:7092/manage/social?year=2520&day=4", email.Text);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.EmailOutbox.CountAsync(e => e.Subject == Subject, CancellationToken));
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        Assert.Contains("Did it go out?", await organizer.GetStringAsync("/manage/social?year=2520&day=4", CancellationToken));
    }

    [Fact]
    public async Task AnInterruptedSeasonPostWaitsForAnOrganizerToCheck()
    {
        // Season 2521's sign-ups are open, and its "sign-ups are open" post to Facebook was interrupted 20 minutes ago.
        await TestSeasons.EnsureAsync(fixture, 2521, claimsOpen: false, season => season.ClaimsOpenAt = SignUpsOpenedAt);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            db.SeasonSocialPosts.Add(new SeasonSocialPost
            {
                SeasonYear = 2521,
                Kind = SeasonPostKind.SignUpsOpen,
                Platform = SocialPlatform.Facebook,
                Status = SocialPostStatus.Posting,
                At = DateTimeOffset.UtcNow.AddMinutes(-20),
                MarkedBy = "automation",
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Automatic);
        try
        {
            // It isn't posted again by itself, in case it went out.
            Assert.Equal(0, await RunSignUpsAsync(2521));
            await using (var db = await fixture.CreateDbContextAsync())
            {
                Assert.Equal(SocialPostStatus.Unconfirmed, (await db.SeasonSocialPosts.SingleAsync(p => p.SeasonYear == 2521, CancellationToken)).Status);
            }

            var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "Did the C# Advent 2521 ");
            Assert.Contains("If it isn't, press Post now.", email.Text);
            Assert.Contains("Did it go out?", await organizer.GetStringAsync("/manage/social/signups?year=2521", CancellationToken));

            // It wasn't there, so an organizer posts it again.
            var posted = await PostSeasonFormAsync(organizer, "post", 2521, SeasonPostKind.SignUpsOpen, SocialPlatform.Facebook);
            Assert.Equal("/manage/social/signups?year=2521", TestBrowser.PathOf(posted.Headers.Location!));
            await using (var db = await fixture.CreateDbContextAsync())
            {
                var post = await db.SeasonSocialPosts.SingleAsync(p => p.SeasonYear == 2521, CancellationToken);
                Assert.Equal((SocialPostStatus.Posted, "organizer", (string?)null), (post.Status, post.MarkedBy, post.Error));
            }
        }
        finally
        {
            await SetModeAsync(organizer, SocialPlatform.Facebook, SocialPostingMode.Manual);
        }
    }

    [Theory]
    [InlineData("social-post-unconfirmed", "Did it go out on Facebook?")]
    [InlineData("social-post-gave-up", "C# Advent tried 3 times to post")]
    public async Task ThePostingAlertsAreInTheEmailCatalog(string key, string text)
    {
        using var client = fixture.CreateWebClient();

        var preview = await client.GetStringAsync($"/dev/emails/{key}", CancellationToken);

        Assert.Contains(text, preview);
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

    private async Task<RunResult> RunAtAsync(DateTimeOffset at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/social/run?at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResult>(CancellationToken))!;
    }

    private async Task<int> RunSignUpsAtAsync(int year, DateTimeOffset at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/social/signups/run?at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}&year={year}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignUpsRun>(CancellationToken))!.Posted;
    }

    /// <summary>A time in the reveal time zone, as UTC.</summary>
    private static DateTimeOffset Eastern(DateTime local)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        return new DateTimeOffset(local, eastern.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Today's date in Eastern time, by the real clock, which is what the pages count days from.</summary>
    private static DateTime TodayInEastern() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, EasternTime).Date;

    /// <summary>
    /// Asserts the page says <paramref name="text"/> for the days from today until <paramref name="opens"/>: 8, as the test
    /// set it up, or one fewer if midnight Eastern passed between setting it up and the page counting them (#198).
    /// </summary>
    private static void AssertContainsDaysUntil(string page, DateTime opens, Func<int, string> text)
    {
        var expected = new[] { 8, (opens.Date - TodayInEastern()).Days }.Distinct().Select(text).ToList();
        Assert.True(expected.Any(page.Contains), $"The page doesn't say \"{string.Join("\" or \"", expected)}\".");
    }

    private async Task<SocialPost> FacebookPostAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.SocialPosts.AsNoTracking().SingleAsync(p => p.SlotId == slotId && p.Platform == SocialPlatform.Facebook, CancellationToken);
    }

    /// <summary>Posts one of the social page's plain per-entry forms, with an antiforgery token from <paramref name="tokenPage"/>.</summary>
    private async Task<HttpResponseMessage> PostSlotFormAsync(HttpClient browser, string tokenPage, string action, int slotId, string returnTo)
    {
        var html = await browser.GetStringAsync(tokenPage, CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["SlotId"] = slotId.ToString(CultureInfo.InvariantCulture),
            ["Platform"] = nameof(SocialPlatform.Facebook),
            ["ReturnUrl"] = returnTo.TrimStart('/'),
        };
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields), CancellationToken);
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

    /// <summary>Runs the "sign-ups are open" posts at noon Eastern on the day sign-ups opened (<see cref="SignUpsOpenedAt"/>).</summary>
    private async Task<int> RunSignUpsAsync(int year)
    {
        var noon = SignUpsOpenedAt.AddHours(3);
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
