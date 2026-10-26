using System.Net.Http.Json;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The blurb check's deadline email and its "a backup is standing by" notice look at the backup round the spot is in now
/// (#234), not at everything its backups ever did. Runs are driven through the development endpoints at chosen times: 6 PM
/// Eastern is 23:00 UTC in December, midnight Eastern 05:00 UTC, and post 1's deadline is its 10 AM Eastern announcement
/// (15:00 UTC). Seasons 2600-2602.
/// </summary>
public sealed class BlurbRoundTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABackupWhoFilledTheSpotInAnEarlierRoundDoesntStopTheDeadlineEmail()
    {
        const int year = 2600;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s is { Day: 7, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1));
        using var xia = await JoinAsync(year, "round-xia-2600", 260001, "Xia Earlier");

        // Round 1, in November: the spot was dropped, and Xia takes it straight away.
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2600-11-20T15:00:00Z"));
        await xia.PostFormAsync(await LatestOfferAsync(year, "Xia Earlier"), "accept", Accept("Xia Earlier", Sample("with-blurb", year)));

        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == year && s.Day == 7 && s.Part == 1, CancellationToken);
            Assert.Equal("Xia Earlier", slot.AuthorName);

            // Later the organizers give it to Nell, whose post doesn't open with the blurb.
            slot.AuthorName = "Nell New";
            slot.ContributorId = null;
            slot.PostTitle = SamplePosts.Title;
            slot.PostUrl = Sample("without-blurb", year);
            slot.Contact!.Email = "nell-2600@example.com";
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
        }

        // At the reveal Nell hears about it, and the backups are asked again (round 2): Xia has filled a spot, so nobody's left.
        await RunBlurbsAsync(year, "2600-12-07T05:30:00Z");
        await fixture.WaitForEmailAsync("nell-2600@example.com", "Please add the C# Advent blurb to your post for December 7");

        // At the deadline nobody's standing by, so the organizers hear, whatever Xia did in round 1.
        Assert.Equal(1, (await RunBlurbsAsync(year, "2600-12-07T15:00:00Z")).Emailed);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.EmailOutbox.CountAsync(e => e.Kind == "blurb-deadline" && e.Text.Contains("December 7, 2600"), CancellationToken));
            Assert.Equal([BackupReason.DroppedOut, BackupReason.BlurbMissing], await db.BackupRounds.Where(r => r.SlotId == slotId).OrderBy(r => r.Number).Select(r => r.Reason).ToListAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task ABackupStandingByForAMissingLinkStillCountsOnceTheLinkArrivesWithoutTheBlurb()
    {
        const int year = 2601;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 8, Part: 1 });
            slot.AuthorName = "Lena Late";
            slot.Contact = new SlotContact { Email = "lena-2601@example.com" };
        });
        using var yuri = await JoinAsync(year, "round-yuri-2601", 260101, "Yuri Ready");

        // The evening before, Lena's link is missing: Yuri is asked, and stands by to take the spot at the reveal.
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2601-12-07T23:00:00Z"));
        await yuri.PostFormAsync(await LatestOfferAsync(year, "Yuri Ready"), "accept", Accept("Yuri Ready", Sample("with-blurb", year)));

        // Then Lena's link arrives, but her post doesn't open with the blurb. No new round starts while Yuri stands by.
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 8 && s.Part == 1, CancellationToken);
            slot.PostTitle = SamplePosts.Title;
            slot.PostUrl = Sample("without-blurb", year);
            await db.SaveChangesAsync(CancellationToken);
            slotId = slot.Id;
        }

        Assert.Equal(new BlurbRunResult(1, 0), await RunBlurbsAsync(year, "2601-12-08T00:00:00Z"));

        // From the reveal Lena hears about it, and the organizers hear that Yuri is standing by.
        Assert.Equal(2, (await RunBlurbsAsync(year, "2601-12-08T05:30:00Z")).Emailed);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var notice = await db.EmailOutbox.SingleAsync(e => e.Kind == "blurb-backup-standing-by" && e.Text.Contains("December 8, 2601"), CancellationToken);
            Assert.Contains("Yuri Ready is standing by", notice.Text);
        }

        // At the deadline Yuri takes the spot, and the organizers aren't told that nobody's standing by.
        Assert.Equal(0, (await RunBlurbsAsync(year, "2601-12-08T15:00:00Z")).Emailed);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal("Yuri Ready", await db.Slots.Where(s => s.Id == slotId).Select(s => s.AuthorName).SingleAsync(CancellationToken));
            Assert.False(await db.EmailOutbox.AnyAsync(e => e.Kind == "blurb-deadline" && e.Text.Contains("December 8, 2601"), CancellationToken));
        }
    }

    [Fact]
    public async Task TheNoticeAboutABackupWhoLeftDoesntCountForTheNextOne()
    {
        const int year = 2602;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 9, Part: 1 });
            slot.AuthorName = "Owen Original";
            slot.PostTitle = SamplePosts.Title;
            slot.PostUrl = Sample("without-blurb", year);
            slot.Contact = new SlotContact { Email = "owen-2602@example.com" };
        });
        using var xavi = await JoinAsync(year, "round-xavi-2602", 260201, "Xavi First");
        using var yara = await JoinAsync(year, "round-yara-2602", 260202, "Yara Second");

        // At the reveal Xavi is asked and stands by, and the organizers hear about him.
        await RunBlurbsAsync(year, "2602-12-09T05:30:00Z");
        await xavi.PostFormAsync(await LatestOfferAsync(year, "Xavi First"), "accept", Accept("Xavi First", Sample("with-blurb", year)));
        Assert.Equal(1, (await RunBlurbsAsync(year, "2602-12-09T06:00:00Z")).Emailed);

        // Then he leaves the backup list, so Yara is asked, and she accepts at 1:20 AM Eastern.
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            await db.Substitutes.Where(s => s.SeasonYear == year && s.Name == "Xavi First").ExecuteDeleteAsync(CancellationToken);
            slotId = await db.Slots.Where(s => s.SeasonYear == year && s.Day == 9 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2602-12-09T06:15:00Z"));
        var offer = await LatestOfferAsync(year, "Yara Second");
        await yara.PostFormAsync(offer, "accept", Accept("Yara Second", Sample("with-blurb", year)));

        // Pages run on the real clock, long before this season: her answer is given the time it would have on the day.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var offerId = int.Parse(offer["/backups/offers/".Length..], System.Globalization.CultureInfo.InvariantCulture);
            await db.BackupOffers.Where(o => o.Id == offerId)
                .ExecuteUpdateAsync(o => o.SetProperty(x => x.RespondedAt, new DateTimeOffset(2602, 12, 9, 6, 20, 0, TimeSpan.Zero)), CancellationToken);
        }

        // The organizers heard about Xavi, not Yara, so at the deadline she doesn't take the spot yet: they hear about her, and
        // she takes it an hour later.
        Assert.Equal(0, (await RunBackupsAsync(year, "2602-12-09T15:00:00Z")).Used);
        Assert.Equal(1, (await RunBlurbsAsync(year, "2602-12-09T15:00:00Z")).Emailed);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var notices = await db.EmailOutbox
                .Where(e => e.Kind == "blurb-backup-standing-by" && e.Text.Contains("December 9, 2602"))
                .OrderBy(e => e.Id)
                .Select(e => e.Text)
                .ToListAsync(CancellationToken);
            Assert.Equal(2, notices.Count);
            Assert.Contains("Xavi First is standing by", notices[0]);
            Assert.Contains("Yara Second is standing by", notices[1]);
            Assert.Contains("They take the spot at 11:00 AM Eastern on December 9", notices[1]);
        }

        Assert.Equal(0, (await RunBackupsAsync(year, "2602-12-09T15:59:00Z")).Used);
        Assert.Equal(1, (await RunBackupsAsync(year, "2602-12-09T16:00:00Z")).Used);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal("Yara Second", await db.Slots.Where(s => s.Id == slotId).Select(s => s.AuthorName).SingleAsync(CancellationToken));
        }
    }

    private async Task<HttpClient> JoinAsync(int year, string login, long githubUserId, string name)
    {
        var browser = await fixture.SignInAsync(login, githubUserId, name);
        var response = await browser.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = name, ["Form.AgreeToTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>The page of the latest ask the named backup got in the season.</summary>
    private async Task<string> LatestOfferAsync(int year, string name)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var id = await db.BackupOffers
            .Where(o => o.Slot.SeasonYear == year && o.Substitute.Name == name)
            .OrderByDescending(o => o.Id)
            .Select(o => o.Id)
            .FirstAsync(CancellationToken);
        return $"/backups/offers/{id}";
    }

    private Task<BackupRunResult> RunBackupsAsync(int year, string at) => RunAsync<BackupRunResult>("backups", year, at);

    private Task<BlurbRunResult> RunBlurbsAsync(int year, string at) => RunAsync<BlurbRunResult>("blurbs", year, at);

    private async Task<T> RunAsync<T>(string what, int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/{what}/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The {what} run at {at} failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(CancellationToken))!;
    }

    private static Dictionary<string, string> Accept(string name, string postUrl) => new()
    {
        ["Form.AuthorName"] = name,
        ["Form.PostTitle"] = $"{name}'s stand-in post",
        ["Form.PostUrl"] = postUrl,
    };

    private static string Sample(string name, int year) => $"https://samples.csadvent.test/{name}?year={year}";

    private sealed record BackupRunResult(int Asked, int Used, int Withdrawn, int Exhausted);

    private sealed record BlurbRunResult(int Checked, int Emailed);
}
