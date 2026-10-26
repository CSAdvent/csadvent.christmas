using System.Net.Http.Json;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The organizers' Today panel on /manage and their morning digest (#171). The digest is driven through the development
/// endpoint at chosen times (8 AM Eastern is 13:00 UTC in December), because pages run on the real clock, long before the test
/// seasons. Seasons 2603 and 2604.
/// </summary>
public sealed class TodayTests(AppHostFixture fixture)
{
    private const string Organizers = "hello@csadvent.christmas";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheMorningDigestGoesToTheOrganizersOnceAt8AmEachDayOfAdvent()
    {
        const int year = 2603;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var ready = season.Slots.Single(s => s is { Day: 6, Part: 1 });
            ready.AuthorName = "Dora Digest";
            ready.PostTitle = "Digest day six";
            ready.PostUrl = "https://example.com/digest-2603-six";
            ready.Contact = new SlotContact { Email = "dora-2603@example.com" };

            var late = season.Slots.Single(s => s is { Day: 6, Part: 2 });
            late.AuthorName = "Pip Pending";
            late.Contact = new SlotContact { Email = "pip-2603@example.com" };

            var tomorrow = season.Slots.Single(s => s is { Day: 7, Part: 1 });
            tomorrow.AuthorName = "Tess Tomorrow";
            tomorrow.PostTitle = "Tomorrow's title 2603";
            tomorrow.PostUrl = "https://example.com/digest-2603-seven";
            tomorrow.Contact = new SlotContact { Email = "tess-2603@example.com" };
        });

        // X gave up on today's first post. (Not Facebook: SocialPostingTests counts every Facebook mark.)
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slotId = await db.Slots.Where(s => s.SeasonYear == year && s.Day == 6 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
            db.SocialPosts.Add(new SocialPost
            {
                SlotId = slotId,
                Platform = SocialPlatform.X,
                Status = SocialPostStatus.Failed,
                At = new DateTimeOffset(2603, 12, 6, 5, 30, 0, TimeSpan.Zero),
                MarkedBy = "automation",
                Error = "Test failure 2603",
                Attempts = 3,
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        // Pip's link is still missing on the day, so a backup has been asked.
        using var backup = await fixture.SignInAsync("digest-bo-2603", 260301, "Bo Digest");
        Assert.Equal(HttpStatusCode.Redirect, (await backup.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = "Bo Digest", ["Form.AgreeToTerms"] = "true" })).StatusCode);
        await RunAsync("backups", year, "2603-12-06T12:00:00Z");

        // Not before 8 AM, then once.
        Assert.Equal(0, await RunDigestAsync(year, "2603-12-06T12:59:00Z"));
        Assert.Equal(1, await RunDigestAsync(year, "2603-12-06T13:00:00Z"));
        Assert.Equal(0, await RunDigestAsync(year, "2603-12-06T13:15:00Z"));

        var digest = await fixture.WaitForEmailAsync(Organizers, $"C# Advent {year}, December 6: the morning digest");
        Assert.Contains("Today, December 6:", digest.Text);
        Assert.Contains("- Post 1: Dora Digest, Digest day six (https://example.com/digest-2603-six). Ready. Blurb not checked yet.", digest.Text);
        Assert.Contains("- Needs attention: Post 2: Pip Pending. No post yet.", digest.Text);
        Assert.Contains("- December 6, post 2: Bo Digest has been asked, and has until 11:00 AM Eastern on December 6 to answer.", digest.Text);
        Assert.Contains("- December 6, post 1 on X: failed (", digest.Text);
        Assert.Contains("Test failure 2603", digest.Text);
        Assert.Contains("Requests waiting:", digest.Text);

        // Tomorrow's post is in, and the digest says no more than that before its reveal.
        Assert.Contains("Tomorrow, December 7:", digest.Text);
        Assert.Contains("- Post 1: Tess Tomorrow. Ready.", digest.Text);
        Assert.DoesNotContain("Tomorrow's title 2603", digest.Text);
        Assert.DoesNotContain("digest-2603-seven", digest.Text);
        Assert.DoesNotContain("tess-2603@example.com", digest.Text);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(
                [new DateOnly(year, 12, 6)],
                await db.SentReminders.Where(r => r.Kind == ReminderKind.AdminMorningDigest && r.Slot!.SeasonYear == year).Select(r => r.RunDate).ToListAsync(CancellationToken));
        }

        // Only on the days of Advent: not the day before, nor after Christmas.
        Assert.Equal(0, await RunDigestAsync(year, "2603-11-30T13:00:00Z"));
        Assert.Equal(0, await RunDigestAsync(year, "2603-12-26T13:00:00Z"));
    }

    [Fact]
    public async Task TheOverviewHasATodayPanel()
    {
        const int year = 2604;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var overview = WebUtility.HtmlDecode(await organizer.GetPageAsync("/manage"));
        Assert.Contains("id=\"today\"", overview);
        Assert.Contains("Requests waiting", overview);
        Assert.Contains("Failed emails", overview);
        Assert.Contains("No C# Advent days today or tomorrow.", overview);

        // A season's own panel, as the test season's rehearsal has (?year=9000).
        Assert.Contains($"C# Advent {year} has no days today or tomorrow.", WebUtility.HtmlDecode(await organizer.GetPageAsync($"/manage?year={year}")));
    }

    private async Task<int> RunDigestAsync(int year, string at) => (await RunAsync("digest", year, at)).GetProperty("sent").GetInt32();

    private async Task<System.Text.Json.JsonElement> RunAsync(string what, int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/{what}/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The {what} run at {at} failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(CancellationToken);
    }
}
