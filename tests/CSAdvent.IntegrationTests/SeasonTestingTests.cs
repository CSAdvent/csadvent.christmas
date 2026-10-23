using System.Globalization;
using System.Net;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>The hidden test season: only testers see it, it follows a rehearsal, and resetting it leaves nothing behind.</summary>
public sealed class SeasonTestingTests(AppHostFixture fixture)
{
    private const string Page = "/manage/testing";
    private const int TestYear = Season.TestYear;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static DateOnly EasternToday =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);

    [Fact]
    public async Task TestersTryTheHiddenTestSeasonAndResettingDeletesEverything()
    {
        // December 1 five days ago in the rehearsal: Day 3 is revealed, and Day 20 is two weeks away.
        using var organizer = await TestSeasonAsync(EasternToday.AddDays(-5));
        var invited = await organizer.PostFormAsync(Page, "tester", new() { ["Tester.Login"] = "@Testing-Tess" });
        Assert.Equal($"{Page}?done=added", TestBrowser.PathOf(invited.Headers.Location!));

        using var tester = await fixture.SignInAsync("testing-tess", 8201);
        using var stranger = await fixture.SignInAsync("testing-stranger", 8202);
        using var anonymous = fixture.CreateWebClient();

        // Testers are invited to switch to test mode; to everyone else the test season doesn't exist.
        Assert.Contains("You're invited to test C# Advent.", await tester.GetStringAsync("/about", CancellationToken));
        Assert.DoesNotContain("test C# Advent", await stranger.GetStringAsync("/about", CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/claim/{TestYear}/20/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/backups/{TestYear}", CancellationToken)).StatusCode);
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

        Assert.Contains("Testing in production", await tester.GetStringAsync($"/{TestYear}/day/3/1", CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/{TestYear}/day/3/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/{TestYear}/day/3/1", CancellationToken)).StatusCode);
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
    public async Task ResettingNeedsConfirming()
    {
        using var organizer = await TestSeasonAsync(EasternToday);

        var unconfirmed = await organizer.PostFormAsync(Page, "reset", new() { ["Reset.Confirm"] = "false" });

        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Tick the box to confirm", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task TheTestSeasonsYearCantBeARealSeason()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var response = await organizer.PostFormAsync("/manage/seasons", "create-season", new() { ["Form.Year"] = $"{TestYear}", ["Form.SlotsPerDay"] = "2" });

        Assert.Contains("Enter a year from 2017 to 8999.", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>Signs in as an organizer, creates the test season if there isn't one yet, and sets its rehearsal.</summary>
    private async Task<HttpClient> TestSeasonAsync(DateOnly december1)
    {
        var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var date = december1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if ((await organizer.GetStringAsync(Page, CancellationToken)).Contains(">Create the test season</button>", StringComparison.Ordinal))
        {
            await organizer.PostFormAsync(Page, "create", new() { ["Create.RehearsalStartsOn"] = date });
        }

        await organizer.PostFormAsync(Page, "move", new() { ["Move.RehearsalStartsOn"] = date });
        return organizer;
    }
}
