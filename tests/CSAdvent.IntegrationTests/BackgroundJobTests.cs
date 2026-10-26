using System.Net.Http.Json;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The background jobs' reliability: one spot that fails doesn't stop a step's other spots (#147), backups are asked again
/// when a spot needs one a second time (#153), and organizers see when each step last succeeded (#172). Runs are driven
/// through the development endpoints at chosen times (6 PM Eastern is 23:00 UTC in December), and spots are made to fail
/// with triggers only the throwaway test database gets. Seasons 2560-2579.
/// </summary>
public sealed class BackgroundJobTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASpotThatFailsDoesntStopTheOtherBackupAsks()
    {
        const int year = 2561;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            season.Slots.Single(s => s is { Day: 3, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1);
            season.Slots.Single(s => s is { Day: 4, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1);
        });
        using var backup = await JoinAsync(year, "job-backup-2561", 256101, "Bo Backup");

        // Asking anyone about December 3 fails every time.
        await FailInsertsAsync("backup_offers", year, day: 3);

        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2561-12-01T12:00:00Z"));
        await fixture.WaitForEmailAsync("job-backup-2561@example.com", "Can you fill in on C# Advent, December 4?");
    }

    [Fact]
    public async Task APostThatFailsDoesntStopTheOtherBlurbChecks()
    {
        const int year = 2562;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            foreach (var part in new[] { 1, 2 })
            {
                var slot = season.Slots.Single(s => s.Day == 3 && s.Part == part);
                slot.AuthorName = $"Author {part}";
                slot.PostTitle = $"Post {part}";
                slot.PostUrl = $"https://samples.csadvent.test/with-blurb?year={year}&part={part}";
                slot.Contact = new SlotContact { Email = $"author-{part}-{year}@example.com" };
            }
        });

        // Checking part 1's post fails every time.
        await FailInsertsAsync("blurb_checks", year, day: 3, part: 1);

        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/blurbs/run?year={year}&at={Uri.EscapeDataString("2562-12-02T12:00:00Z")}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<BlurbRunResult>(CancellationToken))!.Checked);

        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(BlurbStatus.Passed, await db.BlurbChecks.Where(c => c.Slot.SeasonYear == year && c.Slot.Part == 2).Select(c => c.Status).SingleAsync(CancellationToken));
    }

    [Fact]
    public async Task ADroppedSpotGivenToSomeoneWhoseLinkIsMissingAsksEveryoneAgain()
    {
        // #153: dropped in November with nobody taking it, given to a new author, then their link is missing in December.
        const int year = 2560;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s is { Day: 9, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1));
        using var amy = await JoinAsync(year, "job-amy-2560", 256001, "Amy Again");
        using var ben = await JoinAsync(year, "job-ben-2560", 256002, "Ben Again");

        // November: Amy says not this time, Ben doesn't answer, and the organizers hear nobody's left.
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2560-11-05T15:00:00Z"));
        await amy.PostFormAsync(await LatestOfferAsync(year, "Amy Again"), "decline", new());
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2560-11-05T15:15:00Z"));
        Assert.Equal(new BackupRunResult(0, 0, 0, 1), await RunBackupsAsync(year, "2560-11-05T19:15:00Z"));
        Assert.Equal(new BackupRunResult(0, 0, 0, 0), await RunBackupsAsync(year, "2560-11-05T19:30:00Z"));

        // An organizer gives the spot to Nora.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == year && s.Day == 9 && s.Part == 1, CancellationToken);
            slot.AuthorName = "Nora New";
            slot.Contact = new SlotContact { Email = "nora-2560@example.com" };
            await db.SaveChangesAsync(CancellationToken);
        }

        // The evening before her day, her link is missing: a new round, so Amy and Ben are asked again, and the organizers
        // hear again when nobody's left.
        Assert.Equal(new BackupRunResult(0, 0, 0, 0), await RunBackupsAsync(year, "2560-12-08T22:59:00Z"));
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2560-12-08T23:00:00Z"));
        Assert.Equal(2, await fixture.CountEmailsAsync("job-amy-2560@example.com", "Can you fill in on C# Advent, December 9?"));
        Assert.Contains("We still don&#x27;t have the post for December 9", await amy.GetPageAsync(await LatestOfferAsync(year, "Amy Again")));

        await amy.PostFormAsync(await LatestOfferAsync(year, "Amy Again"), "decline", new());
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2560-12-08T23:15:00Z"));
        Assert.Equal(2, await fixture.CountEmailsAsync("job-ben-2560@example.com", "Can you fill in on C# Advent, December 9?"));
        Assert.Equal(new BackupRunResult(0, 0, 0, 1), await RunBackupsAsync(year, "2560-12-09T03:15:00Z"));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var rounds = await db.BackupRounds.Where(r => r.Slot.SeasonYear == year).OrderBy(r => r.Number).ToListAsync(CancellationToken);
            Assert.Equal([BackupReason.DroppedOut, BackupReason.LinkMissing], rounds.Select(r => r.Reason));
            Assert.All(rounds, r => Assert.NotNull(r.NobodyLeftAt));
            Assert.Equal(2, await db.EmailOutbox.CountAsync(e => e.Kind == "backup-none-left" && e.Text.Contains("December 9, 2560"), CancellationToken));
        }
    }

    [Fact]
    public async Task WhenABackupWhoFilledASpotDropsOutTheOthersAreAskedAgainAndCanTakeIt()
    {
        const int year = 2563;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s is { Day: 10, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1));
        using var amy = await JoinAsync(year, "job-amy-2563", 256301, "Amy Again");
        using var ben = await JoinAsync(year, "job-ben-2563", 256302, "Ben Again");

        // Amy doesn't answer in time, Ben says not this time, then Amy takes it after all.
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2563-11-05T15:00:00Z"));
        Assert.Equal(new BackupRunResult(1, 0, 0, 0), await RunBackupsAsync(year, "2563-11-05T19:00:00Z"));
        await ben.PostFormAsync(await LatestOfferAsync(year, "Ben Again"), "decline", new());
        await amy.PostFormAsync(await LatestOfferAsync(year, "Amy Again"), "accept", Accept("Amy Again", "https://example.com/amy-2563"));

        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 10 && s.Part == 1, CancellationToken);
            Assert.Equal("Amy Again", slot.AuthorName);
            slotId = slot.Id;
        }

        // Then she has to drop out too. Ben is asked again, and nobody's stepped in since, so he can take it.
        await amy.PostFormAsync($"/my-spots/{slotId}", "dropout", new() { ["DropOut.Confirm"] = "true" });
        Assert.Equal(2, await fixture.CountEmailsAsync("job-ben-2563@example.com", "Can you fill in on C# Advent, December 10?"));
        var offer = await LatestOfferAsync(year, "Ben Again");
        Assert.Contains("had to drop out, so the spot is open", await ben.GetPageAsync(offer));

        var accepted = await ben.PostFormAsync(offer, "accept", Accept("Ben Again", "https://example.com/ben-2563"));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal("Ben Again", await db.Slots.Where(s => s.Id == slotId).Select(s => s.AuthorName).SingleAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task OrganizersSeeWhenEachStepLastSucceeded()
    {
        // In the Testing environment only the email queue's worker and the test season's run: the rest are off, and never
        // make the health check Degraded.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            for (var attempt = 0; attempt < 100 && !await db.JobHeartbeats.AnyAsync(h => h.Step == "email-queue" && h.LastSucceededAt != null, CancellationToken); attempt++)
            {
                await Task.Delay(100, CancellationToken);
            }

            Assert.True(await db.JobHeartbeats.AnyAsync(h => h.Step == "email-queue" && h.LastSucceededAt != null, CancellationToken), "The email queue's runs weren't saved.");
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var overview = await organizer.GetPageAsync("/manage");
        Assert.Contains(">Background jobs</h2>", overview);
        Assert.Contains("Reminders and the authors&#x27; daily email", overview);
        Assert.Contains("Off here", overview);
        Assert.DoesNotContain("Late:", overview);

        using var client = fixture.CreateWebClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health", CancellationToken));
    }

    /// <summary>
    /// Makes inserting into <paramref name="table"/> (a table with a <c>slot_id</c>) fail for one day of one season, with an
    /// error none of the code expects. The trigger is only in the throwaway test database.
    /// </summary>
    private async Task FailInsertsAsync(string table, int year, int day, int? part = null)
    {
        var name = $"test_fail_{table}_{year}";
        var onlyPart = part is { } p ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" AND s.part = {p}") : "";
        var sql = $"""
            CREATE OR REPLACE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (SELECT 1 FROM slots s WHERE s.id = NEW.slot_id AND s.season_year = {year} AND s.day = {day}{onlyPart}) THEN
                    RAISE EXCEPTION 'Test failure for slot %', NEW.slot_id;
                END IF;
                RETURN NEW;
            END $$;
            DROP TRIGGER IF EXISTS {name} ON {table};
            CREATE TRIGGER {name} BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION {name}();
            """;
        await using var db = await fixture.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(sql, CancellationToken);
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

    private async Task<BackupRunResult> RunBackupsAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/backups/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The backup run at {at} failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<BackupRunResult>(CancellationToken))!;
    }

    private static Dictionary<string, string> Accept(string name, string postUrl) => new()
    {
        ["Form.AuthorName"] = name,
        ["Form.PostTitle"] = $"{name}'s stand-in post",
        ["Form.PostUrl"] = postUrl,
    };

    private sealed record BackupRunResult(int Asked, int Used, int Withdrawn, int Exhausted);

    private sealed record BlurbRunResult(int Checked, int Emailed);
}
