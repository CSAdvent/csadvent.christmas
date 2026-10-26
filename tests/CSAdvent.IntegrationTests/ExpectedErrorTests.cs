using System.Globalization;
using System.Net.Http.Json;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Errors the code expects, which mustn't trip the error alert (#157, #224). Runs that used to insert a record on every run
/// and leave the unique index to refuse the ones already there, which EF Core logs as errors, now skip what's recorded. A
/// trigger only the throwaway test database gets makes inserting a record that's already there fail outright for these
/// seasons (<see cref="RefuseDuplicatesAsync"/>), so a run that stops skipping fails these tests. Seasons 2400-2419.
/// </summary>
public sealed class ExpectedErrorTests(AppHostFixture fixture)
{
    private static readonly SemaphoreSlim TriggersCreated = new(1, 1);
    private static bool _triggersExist;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RemindersAlreadySentArentInsertedAgain()
    {
        const int year = 2400;
        await RefuseDuplicatesAsync();
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Claim(season, day: 3, part: 1, "Ria Ready", "ria-2400@example.com", "Ria's 2400 post");
            Claim(season, day: 3, part: 2, "Sid Second", "sid-2400@example.com", "Sid's 2400 post");
            Claim(season, day: 8, part: 1, "Lu Late", "lu-2400@example.com");
        });

        // December 1 is 7 days before Lu's day: her post link reminder, then nothing later that day.
        Assert.Equal(new ReminderRun(1, 0, 0), await RunRemindersAsync("2400-12-01T15:00:00Z"));
        Assert.Equal(new ReminderRun(0, 0, 0), await RunRemindersAsync("2400-12-01T19:00:00Z"));

        // December 3: Lu's 5-day reminder and the daily email to all three, then nothing later that day.
        Assert.Equal(new ReminderRun(1, 3, 0), await RunRemindersAsync("2400-12-03T15:00:00Z"));
        Assert.Equal(new ReminderRun(0, 0, 0), await RunRemindersAsync("2400-12-03T19:00:00Z"));

        // December 8: Lu's last call, once.
        Assert.Equal(new ReminderRun(0, 0, 1), await RunRemindersAsync("2400-12-08T12:30:00Z"));
        Assert.Equal(new ReminderRun(0, 0, 0), await RunRemindersAsync("2400-12-08T13:00:00Z"));

        Assert.Equal(1, await fixture.CountEmailsAsync("ria-2400@example.com", "Day 3 of C# Advent: 2 new posts to share"));
        Assert.Equal(1, await fixture.CountEmailsAsync("lu-2400@example.com", "Your C# Advent post is due in 7 days (December 8)"));
    }

    [Fact]
    public async Task NoBackupsLeftIsntInsertedAgain()
    {
        const int year = 2401;
        await RefuseDuplicatesAsync();
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s.Day == 9 && s.Part == 1).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1));

        // Nobody's on the backup list, so the organizers hear once, and every later run until the day's over skips it.
        Assert.Equal(new BackupRun(0, 0, 0, 1), await RunBackupsAsync(year, "2401-12-08T12:00:00Z"));
        Assert.Equal(new BackupRun(0, 0, 0, 0), await RunBackupsAsync(year, "2401-12-08T12:15:00Z"));
        Assert.Equal(new BackupRun(0, 0, 0, 0), await RunBackupsAsync(year, "2401-12-09T09:00:00Z"));

        // The email goes to the organizers' inbox, which every test shares, so it's counted in the queue instead.
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(1, await db.EmailOutbox.CountAsync(e => e.Kind == "backup-none-left" && e.Text.Contains("December 9, 2401"), CancellationToken));
        Assert.Equal(1, await db.SentReminders.CountAsync(r => r.Kind == ReminderKind.AdminBackupsExhausted && r.Slot!.SeasonYear == year, CancellationToken));
    }

    [Fact]
    public async Task TheSignUpsEmailIsntInsertedAgain()
    {
        const int year = 2402;
        await RefuseDuplicatesAsync();
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);

        // Every run for two days after sign-ups open gets this far.
        await RunAnnouncementsAsync(year);
        await RunAnnouncementsAsync(year);
        await RunAnnouncementsAsync(year);

        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(1, await db.Announcements.CountAsync(a => a.Kind == AnnouncementKind.SignUpsOpen && a.SeasonYear == year, CancellationToken));
    }

    [Fact]
    public async Task RejoiningTheBackupListDoesntRecordItsEmailAgain()
    {
        const int year = 2403;
        await RefuseDuplicatesAsync();
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var backup = await fixture.SignInAsync("eet-rejoin", 240301, "Re Joiner");

        await JoinBackupsAsync(backup, year);
        await backup.PostFormAsync($"/backups/{year}", "leave", new() { ["Leave.Confirm"] = "true" });
        var rejoined = await JoinBackupsAsync(backup, year);

        Assert.Equal($"/backups/{year}?done=rejoined", TestBrowser.PathOf(rejoined.Headers.Location!));
        Assert.Equal(1, await fixture.CountEmailsAsync("eet-rejoin@example.com", $"You're on the C# Advent {year} backup list"));
    }

    private static void Claim(Season season, int day, int part, string author, string email, string? title = null)
    {
        var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
        slot.AuthorName = author;
        slot.PostTitle = title;
        slot.PostUrl = title is null ? null : $"https://example.com/{author.Split(' ')[0].ToLowerInvariant()}-2400";
        slot.Contact = new SlotContact { Email = email };
    }

    private async Task<ReminderRun> RunRemindersAsync(string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/reminders/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The reminder run at {at} failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<ReminderRun>(CancellationToken))!;
    }

    private async Task<BackupRun> RunBackupsAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/backups/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The backup run at {at} failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<BackupRun>(CancellationToken))!;
    }

    /// <summary>Runs announcements at noon Eastern today, after the morning send time, whatever time the tests run.</summary>
    private async Task RunAnnouncementsAsync(int year)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date;
        var noon = new DateTimeOffset(today.AddHours(12), eastern.GetUtcOffset(today.AddHours(12))).ToUniversalTime();
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/announcements/run?at={Uri.EscapeDataString(noon.ToString("O", CultureInfo.InvariantCulture))}&year={year}", null, CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"The announcement run failed: {await response.Content.ReadAsStringAsync(CancellationToken)}");
    }

    private static async Task<HttpResponseMessage> JoinBackupsAsync(HttpClient browser, int year)
    {
        var response = await browser.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = "Re Joiner", ["Form.AgreeToTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    /// <summary>
    /// Makes inserting a sent reminder, a "sign-ups are open" announcement or a backup sign-up email that's already recorded
    /// fail with an error the code doesn't handle, for seasons 2400-2419 only, instead of the unique violation it does. The
    /// triggers are created once, in the throwaway test database only.
    /// </summary>
    private async Task RefuseDuplicatesAsync()
    {
        await TriggersCreated.WaitAsync(CancellationToken);
        try
        {
            if (_triggersExist)
            {
                return;
            }

            await using var db = await fixture.CreateDbContextAsync();
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION test_refuse_duplicate_reminder() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM slots s WHERE s.id = NEW.slot_id AND s.season_year BETWEEN 2400 AND 2419)
                       AND EXISTS (SELECT 1 FROM sent_reminders r WHERE r.kind = NEW.kind AND r.run_date = NEW.run_date AND r.slot_id = NEW.slot_id) THEN
                        RAISE EXCEPTION 'The % reminder for slot % on % was inserted again', NEW.kind, NEW.slot_id, NEW.run_date;
                    END IF;
                    RETURN NEW;
                END $$;
                DROP TRIGGER IF EXISTS test_refuse_duplicate_reminder ON sent_reminders;
                CREATE TRIGGER test_refuse_duplicate_reminder BEFORE INSERT ON sent_reminders FOR EACH ROW EXECUTE FUNCTION test_refuse_duplicate_reminder();

                CREATE OR REPLACE FUNCTION test_refuse_duplicate_announcement() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.season_year BETWEEN 2400 AND 2419
                       AND EXISTS (SELECT 1 FROM announcements a WHERE a.kind = NEW.kind AND a.season_year = NEW.season_year) THEN
                        RAISE EXCEPTION 'The % announcement for % was inserted again', NEW.kind, NEW.season_year;
                    END IF;
                    RETURN NEW;
                END $$;
                DROP TRIGGER IF EXISTS test_refuse_duplicate_announcement ON announcements;
                CREATE TRIGGER test_refuse_duplicate_announcement BEFORE INSERT ON announcements FOR EACH ROW EXECUTE FUNCTION test_refuse_duplicate_announcement();

                CREATE OR REPLACE FUNCTION test_refuse_duplicate_backup_signup_email() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.season_year BETWEEN 2400 AND 2419
                       AND EXISTS (SELECT 1 FROM backup_signup_emails e WHERE e.season_year = NEW.season_year AND e.contributor_id = NEW.contributor_id) THEN
                        RAISE EXCEPTION 'The backup sign-up email for contributor % in % was inserted again', NEW.contributor_id, NEW.season_year;
                    END IF;
                    RETURN NEW;
                END $$;
                DROP TRIGGER IF EXISTS test_refuse_duplicate_backup_signup_email ON backup_signup_emails;
                CREATE TRIGGER test_refuse_duplicate_backup_signup_email BEFORE INSERT ON backup_signup_emails FOR EACH ROW EXECUTE FUNCTION test_refuse_duplicate_backup_signup_email();
                """,
                CancellationToken);
            _triggersExist = true;
        }
        finally
        {
            TriggersCreated.Release();
        }
    }

    private sealed record ReminderRun(int PostLinkReminders, int DailyShares, int DayOfReminders);

    private sealed record BackupRun(int Asked, int Used, int Withdrawn, int Exhausted);
}
