using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Testing a season before its sign-ups open: only testers take part, it follows a rehearsal, and finishing leaves nothing behind.</summary>
public sealed class SeasonTestingTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static DateOnly EasternToday =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);

    [Fact]
    public async Task TestersTryTheSeasonAndFinishingDeletesEverything()
    {
        await TestSeasons.EnsureAsync(fixture, 2120, claimsOpen: false);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        const string page = "/manage/testing?year=2120";

        // December 1 five days ago in the rehearsal: Day 3 is revealed, and Day 20 is two weeks away.
        var rehearsal = EasternToday.AddDays(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var started = await organizer.PostFormAsync(page, "start", new() { ["Start.RehearsalStartsOn"] = rehearsal });
        Assert.Equal($"{page}&done=started", TestBrowser.PathOf(started.Headers.Location!));

        var invited = await organizer.PostFormAsync(page, "tester", new() { ["Tester.Login"] = "@Testing-Tess" });
        Assert.Equal($"{page}&done=added", TestBrowser.PathOf(invited.Headers.Location!));

        using var tester = await fixture.SignInAsync("testing-tess", 8201);
        using var stranger = await fixture.SignInAsync("testing-stranger", 8202);
        using var anonymous = fixture.CreateWebClient();

        // Only testers see the banner and can claim; to everyone else sign-ups haven't opened.
        Assert.Contains("Testing C# Advent 2120.", await tester.GetStringAsync("/about", CancellationToken));
        Assert.DoesNotContain("Testing C# Advent", await stranger.GetStringAsync("/about", CancellationToken));
        Assert.Contains("Sign-ups aren", await stranger.GetStringAsync("/claim/2120/20/1", CancellationToken));

        var claimed = await tester.PostFormAsync("/claim/2120/20/1", "claim", new()
        {
            ["Form.DisplayName"] = "Tess Tester",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });
        Assert.Equal("/claim/requested/2120/20/1", TestBrowser.PathOf(claimed.Headers.Location!));

        // A revealed test entry is there for testers and nobody else, and never in a feed.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == 2120 && s.Day == 3 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Tess Tester";
            slot.PostTitle = "Testing in production";
            slot.PostUrl = "https://example.com/testing-in-production";
            await db.SaveChangesAsync(CancellationToken);
        }

        Assert.Contains("Testing in production", await tester.GetStringAsync("/2120/day/3/1", CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/2120/day/3/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/2120/day/3/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/cards/2120/3/1.jpg", CancellationToken)).StatusCode);
        Assert.DoesNotContain("Testing in production", await tester.GetStringAsync("/archive/2120/rss", CancellationToken));

        // Finishing deletes it all, and the season goes back to waiting for its sign-ups.
        Assert.Contains("1 request", await organizer.GetStringAsync(page, CancellationToken));
        var finished = await organizer.PostFormAsync(page, "finish", new() { ["Finish.Confirm"] = "true" });
        Assert.Equal($"{page}&done=finished", TestBrowser.PathOf(finished.Headers.Location!));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = await db.Seasons.AsNoTracking().SingleAsync(s => s.Year == 2120, CancellationToken);
            Assert.Null(season.TestingStartedAt);
            Assert.Null(season.RehearsalStartsOn);
            Assert.False(await db.Slots.AnyAsync(s => s.SeasonYear == 2120 && (s.AuthorName != null || s.PostUrl != null), CancellationToken));
            Assert.False(await db.ClaimRequests.AnyAsync(r => r.Slot.SeasonYear == 2120, CancellationToken));
            Assert.True(await db.SeasonTesters.AnyAsync(t => t.SeasonYear == 2120 && t.GithubLogin == "testing-tess", CancellationToken));
        }

        Assert.DoesNotContain("Testing C# Advent", await tester.GetStringAsync("/about", CancellationToken));
        Assert.Contains("Sign-ups aren", await tester.GetStringAsync("/claim/2120/20/1", CancellationToken));
    }

    [Fact]
    public async Task FinishingNeedsConfirming()
    {
        await TestSeasons.EnsureAsync(fixture, 2122, claimsOpen: false);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        const string page = "/manage/testing?year=2122";
        await organizer.PostFormAsync(page, "start", new() { ["Start.RehearsalStartsOn"] = EasternToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

        var unconfirmed = await organizer.PostFormAsync(page, "finish", new() { ["Finish.Confirm"] = "false" });

        Assert.Contains("Tick the box to confirm", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.NotNull((await db.Seasons.AsNoTracking().SingleAsync(s => s.Year == 2122, CancellationToken)).TestingStartedAt);
    }

    [Fact]
    public async Task ASeasonWithSignUpsOpenCantBeTested()
    {
        await TestSeasons.EnsureAsync(fixture, 2121, claimsOpen: true);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var html = await organizer.GetStringAsync("/manage/testing?year=2121", CancellationToken);

        Assert.Contains("are already open, so it can", html);
        Assert.DoesNotContain(">Start testing</button>", html);
    }
}
