using System.Globalization;
using System.Net;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>The hidden test season: only testers see it, it follows a rehearsal, and resetting it leaves nothing behind.</summary>
public sealed class SeasonTestingTests(AppHostFixture fixture)
{
    private const string Page = "/manage/testing";
    private const int TestYear = Season.TestYear;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestersTryTheHiddenTestSeasonAndResettingDeletesEverything()
    {
        // Today is December 6 in the rehearsal: Day 3 is revealed, and Day 20 is two weeks away.
        using var organizer = await TestSeasonAsync(todayIs: 5);
        Assert.Contains("today is <strong>December 6</strong>", await organizer.GetStringAsync(Page, CancellationToken));
        var invited = await organizer.PostFormAsync(Page, "tester", new() { ["Tester.Login"] = "@Testing-Tess" });
        Assert.Equal($"{Page}?done=added", TestBrowser.PathOf(invited.Headers.Location!));

        using var tester = await fixture.SignInAsync("testing-tess", 8201);
        using var stranger = await fixture.SignInAsync("testing-stranger", 8202);
        using var anonymous = fixture.CreateWebClient();

        // Testers are invited to switch to test mode; to everyone else the test season doesn't exist.
        Assert.Contains("You're invited to test C# Advent.", await tester.GetStringAsync("/about", CancellationToken));
        Assert.DoesNotContain("test C# Advent", await stranger.GetStringAsync("/about", CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/claim/{TestYear}/20/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/backups/{TestYear}", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/cards/{TestYear}.jpg", CancellationToken)).StatusCode);

        var claimed = await tester.PostFormAsync($"/claim/{TestYear}/20/1", "claim", new()
        {
            ["Form.DisplayName"] = "Tess Tester",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });
        Assert.Equal($"/claim/requested/{TestYear}/20/1", TestBrowser.PathOf(claimed.Headers.Location!));

        // A revealed test entry is there for testers and nobody else, and never in a feed.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == TestYear && s.Day == 3 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Tess Tester";
            slot.PostTitle = "Testing in production";
            slot.PostUrl = "https://example.com/testing-in-production";
            await db.SaveChangesAsync(CancellationToken);
        }

        // Testers following its link go straight to the post, like readers, and organizers get its page. Nobody else finds it,
        // link previews included.
        var followed = await tester.GetAsync($"/{TestYear}/day/3/1", CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, followed.StatusCode);
        Assert.Equal("https://example.com/testing-in-production", followed.Headers.Location!.OriginalString);
        Assert.Contains("Testing in production", await organizer.GetStringAsync($"/{TestYear}/day/3/1", CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/{TestYear}/day/3/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/{TestYear}/day/3/1", CancellationToken)).StatusCode);
        using (var preview = fixture.CreateLinkPreview())
        {
            Assert.Equal(HttpStatusCode.NotFound, (await preview.GetAsync($"/{TestYear}/day/3/1", CancellationToken)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/cards/{TestYear}/3/1.jpg", CancellationToken)).StatusCode);
        Assert.DoesNotContain("Testing in production", await tester.GetStringAsync("/rss", CancellationToken));

        // Test mode puts the test calendar on a tester's home page, until they switch back. It does nothing for anyone else.
        Assert.Equal("/", TestBrowser.PathOf((await tester.GetAsync("/test-mode/on?returnUrl=%2F", CancellationToken)).Headers.Location!));
        var testHome = await tester.GetStringAsync("/", CancellationToken);
        Assert.Contains("C# Advent test season</h1>", testHome);
        Assert.Contains("Test mode.", testHome);
        await tester.GetAsync("/test-mode/off?returnUrl=%2F", CancellationToken);
        Assert.DoesNotContain("C# Advent test season", await tester.GetStringAsync("/", CancellationToken));

        await stranger.GetAsync("/test-mode/on?returnUrl=%2F", CancellationToken);
        Assert.DoesNotContain("C# Advent test season", await stranger.GetStringAsync("/", CancellationToken));

        // The organizer pages keep to the real seasons.
        Assert.DoesNotContain($"year={TestYear}", await organizer.GetStringAsync("/manage", CancellationToken));

        // Resetting deletes everything done in it, and leaves the season, its rehearsal and its testers.
        Assert.Contains("1 request", await organizer.GetStringAsync(Page, CancellationToken));
        var reset = await organizer.PostFormAsync(Page, "reset", new() { ["Reset.Confirm"] = "true" });
        Assert.Equal($"{Page}?done=reset", TestBrowser.PathOf(reset.Headers.Location!));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.False(await db.Slots.AnyAsync(s => s.SeasonYear == TestYear && (s.AuthorName != null || s.PostUrl != null), CancellationToken));
            Assert.False(await db.ClaimRequests.AnyAsync(r => r.Slot.SeasonYear == TestYear, CancellationToken));
            Assert.True(await db.SeasonTesters.AnyAsync(t => t.SeasonYear == TestYear && t.GithubLogin == "testing-tess", CancellationToken));
        }

        Assert.Contains("name=\"Form.DisplayName\"", await tester.GetStringAsync($"/claim/{TestYear}/20/1", CancellationToken));
    }

    [Fact]
    public async Task MovingTheRehearsalSendsTheNewDaysReminders()
    {
        // Today is December 18 in the rehearsal, and Day 20's author hasn't sent their post's link.
        using var organizer = await TestSeasonAsync(todayIs: 17);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == TestYear && s.Day == 20 && s.Part == 2, CancellationToken);
            slot.AuthorName = "Rehearsal Rae";
            slot.PostUrl = null;
            slot.Contact ??= new SlotContact { Email = "rae-rehearsal@example.com" };
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunRemindersAsync();
        await fixture.WaitForEmailAsync("rae-rehearsal@example.com", "Your C# Advent post is due in 2 days (December 20)");

        // Moving to the next day on the same real day sends that day's reminder: it isn't taken as already sent.
        await organizer.PostFormAsync(Page, "move", new() { ["Move.TodayIs"] = "18" });
        await RunRemindersAsync();
        await fixture.WaitForEmailAsync("rae-rehearsal@example.com", "Your C# Advent post is due tomorrow (December 20)");
    }

    [Fact]
    public async Task TheDailyEmailNamesTheRehearsalsDay()
    {
        // Today is December 13 in the rehearsal, whatever the real date, and Day 13 has a post.
        using var organizer = await TestSeasonAsync(todayIs: 12);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == TestYear && s.Day == 13 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Thirteen Theo";
            slot.PostTitle = "Rehearsing day thirteen";
            slot.PostUrl = "https://example.com/day-thirteen";
            slot.Contact ??= new SlotContact { Email = "theo-rehearsal@example.com" };
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunRemindersAsync();

        await fixture.WaitForEmailAsync("theo-rehearsal@example.com", "Day 13 of C# Advent: Rehearsing day thirteen");
    }

    [Fact]
    public async Task SendingDueNowChecksTheTestSeasonsPostsForTheBlurbFirst()
    {
        // Today is December 8 in the rehearsal, and Day 8's post links back but doesn't say it's part of C# Advent.
        using var organizer = await TestSeasonAsync(todayIs: 7);
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == TestYear && s.Day == 8 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Blurbless Bo";
            slot.PostTitle = "Rehearsing the blurb";
            slot.PostUrl = $"https://samples.csadvent.test/link-only?year={TestYear}";
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
        }

        var ran = await organizer.PostFormAsync(Page, "run", []);

        // The blurb check ran first, as the worker's does (#162), so the reminders and backups saw its answer.
        Assert.Equal($"{Page}?done=ran", TestBrowser.PathOf(ran.Headers.Location!));
        Assert.Contains("The test season&#x27;s posts have been checked for the blurb", await organizer.GetStringAsync($"{Page}?done=ran", CancellationToken));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(BlurbStatus.Missing, (await db.BlurbChecks.SingleAsync(c => c.SlotId == slotId, CancellationToken)).Status);
        }
    }

    [Fact]
    public async Task ResettingNeedsConfirming()
    {
        using var organizer = await TestSeasonAsync(todayIs: 0);

        var unconfirmed = await organizer.PostFormAsync(Page, "reset", new() { ["Reset.Confirm"] = "false" });

        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Tick the box to confirm", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task EndingTestingDeletesTheTestSeason()
    {
        using var organizer = await TestSeasonAsync(todayIs: 0);
        await organizer.PostFormAsync(Page, "tester", new() { ["Tester.Login"] = "testing-ted" });
        using var tester = await fixture.SignInAsync("testing-ted", 8203);
        await tester.GetAsync("/test-mode/on?returnUrl=%2F", CancellationToken);
        Assert.Contains("Test mode.", await tester.GetStringAsync("/", CancellationToken));

        var unconfirmed = await organizer.PostFormAsync(Page, "end", new() { ["End.Confirm"] = "false" });
        Assert.Contains("Tick the box to confirm that the test season should be deleted.", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));

        var ended = await organizer.PostFormAsync(Page, "end", new() { ["End.Confirm"] = "true" });
        Assert.Equal($"{Page}?done=ended", TestBrowser.PathOf(ended.Headers.Location!));
        Assert.Contains(">Create the test season</button>", await organizer.GetStringAsync(Page, CancellationToken));

        // A tester still in test mode just sees the real site.
        var home = await tester.GetStringAsync("/", CancellationToken);
        Assert.DoesNotContain("Test mode.", home);
        Assert.DoesNotContain("C# Advent test season", home);
        Assert.Equal(HttpStatusCode.NotFound, (await tester.GetAsync($"/claim/{TestYear}/20/1", CancellationToken)).StatusCode);

        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.Seasons.AnyAsync(s => s.Year == TestYear, CancellationToken));
        Assert.False(await db.Slots.AnyAsync(s => s.SeasonYear == TestYear, CancellationToken));
        Assert.False(await db.SeasonTesters.AnyAsync(t => t.SeasonYear == TestYear, CancellationToken));
    }

    [Fact]
    public async Task TheTestSeasonsOtherAdventsAreOnlyForItsRehearsal()
    {
        using var organizer = await TestSeasonAsync(todayIs: 0);
        await organizer.PostFormAsync(Page, "tester", new() { ["Tester.Login"] = "testing-otto" });
        await organizer.PostFormAsync($"/manage/other-advents?year={TestYear}", "add", new()
        {
            ["Form.Name"] = "Rehearsal Advent",
            ["Form.Url"] = "https://example.com/rehearsal-advent",
        });

        try
        {
            // With no current season (every real one finished), the test season's list used to win as the latest (#190).
            await using (var db = await fixture.CreateDbContextAsync())
            {
                var latest = await new OtherAdventsService(db).GetForPageAsync(year: null, CancellationToken);
                Assert.NotEqual(TestYear, latest.Year);
                Assert.DoesNotContain(latest.Items, advent => advent.Name == "Rehearsal Advent");
            }

            using var anonymous = fixture.CreateWebClient();
            Assert.DoesNotContain("Rehearsal Advent", await anonymous.GetStringAsync("/others", CancellationToken));

            // A tester sees it in test mode, as part of the rehearsal, and only then.
            using var tester = await fixture.SignInAsync("testing-otto", 8204);
            Assert.DoesNotContain("Rehearsal Advent", await tester.GetStringAsync("/others", CancellationToken));
            await tester.GetAsync("/test-mode/on?returnUrl=%2F", CancellationToken);
            Assert.Contains("Rehearsal Advent", await tester.GetStringAsync("/others", CancellationToken));
        }
        finally
        {
            await using var db = await fixture.CreateDbContextAsync();
            await db.OtherAdvents.Where(a => a.SeasonYear == TestYear).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TheTestSeasonsYearCantBeARealSeason()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var response = await organizer.PostFormAsync("/manage/seasons", "create-season", new() { ["Form.Year"] = $"{TestYear}", ["Form.SlotsPerDay"] = "2" });

        Assert.Contains("Enter a year from 2017 to 8999.", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>
    /// Runs the reminders at 3 PM Eastern today, whatever time the tests run: after they're all due, including a daily email
    /// that waits until 2 PM for a day's missing post.
    /// </summary>
    private async Task RunRemindersAsync()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date;
        var afternoon = new DateTimeOffset(today.AddHours(15), eastern.GetUtcOffset(today.AddHours(15)));
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/reminders/run?at={Uri.EscapeDataString(afternoon.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Signs in as an organizer, creates the test season if there isn't one yet, and makes today the given day of its
    /// rehearsal (0 is December 1).
    /// </summary>
    private async Task<HttpClient> TestSeasonAsync(int todayIs)
    {
        var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var day = todayIs.ToString(CultureInfo.InvariantCulture);
        if ((await organizer.GetStringAsync(Page, CancellationToken)).Contains(">Create the test season</button>", StringComparison.Ordinal))
        {
            await organizer.PostFormAsync(Page, "create", new() { ["Create.TodayIs"] = day });
        }

        await organizer.PostFormAsync(Page, "move", new() { ["Move.TodayIs"] = day });
        return organizer;
    }
}
