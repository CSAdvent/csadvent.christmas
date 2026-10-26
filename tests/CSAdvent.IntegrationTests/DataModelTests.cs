using System.Net;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The data model's own rules (#188, #245): a spot that gets an author isn't dropped out any more, and one with an author can't
/// be, a spot without an author isn't anyone's, and an announcement's season exists. Seasons 2642-2650: 2645 and 2650 are only
/// in databases of their own, and 2646 to 2648 never exist in the test database.
/// </summary>
public sealed class DataModelTests(AppHostFixture fixture)
{
    /// <summary>The migration that adds the rules, and fixes the rows that would break them first.</summary>
    private const string Migration = "TightenDataModel";

    /// <summary>The migration that adds the rule about dropped-out spots, which waited for a later release (#245).</summary>
    private const string DropOutMigration = "AddAuthorNotDroppedOutCheck";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApprovingARequestIntoADroppedSpotClearsTheDropOut()
    {
        const int year = 2642;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s is { Day: 5, Part: 1 }).DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1));
        using (var contributor = await fixture.SignInAsync("data-model-taker", 264201))
        {
            var requested = await contributor.PostFormAsync($"/claim/{year}/5/1", "claim", new()
            {
                ["Form.DisplayName"] = "Dot Taker",
                ["Form.AgreeToTiming"] = "true",
                ["Form.AgreeToTerms"] = "true",
            });
            Assert.Equal(HttpStatusCode.Redirect, requested.StatusCode);
        }

        var slotId = await SlotIdAsync(year, 5, 1);
        int requestId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            requestId = await db.ClaimRequests.Where(r => r.SlotId == slotId).Select(r => r.Id).SingleAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var approved = await organizer.PostFormAsync($"/manage/requests/{requestId}", "approve", new() { ["Approve.SlotId"] = $"{slotId}" });

        Assert.Equal($"/manage/requests/{requestId}?done=approved", TestBrowser.PathOf(approved.Headers.Location!));
        var slot = await LoadAsync(slotId);
        Assert.Equal(("Dot Taker", (DateTimeOffset?)null), (slot.AuthorName, slot.DroppedOutAt));
    }

    [Fact]
    public async Task AnOrganizerFillingADroppedSpotClearsTheDropOut()
    {
        const int year = 2643;
        var droppedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            season.Slots.Single(s => s is { Day: 6, Part: 1 }).DroppedOutAt = droppedAt;
            season.Slots.Single(s => s is { Day: 7, Part: 1 }).DroppedOutAt = droppedAt;
        });
        var filled = await SlotIdAsync(year, 6, 1);
        var stillOpen = await SlotIdAsync(year, 7, 1);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        await organizer.PostFormAsync($"/manage/entries/{filled}", "entry", new() { ["Form.AuthorName"] = "Sam Substitute" });

        // Saved without an author (only a contact for now), it's still open and dropped, so backups are still asked.
        await organizer.PostFormAsync($"/manage/entries/{stillOpen}", "entry", new() { ["Form.ContactEmail"] = "data-model-later@example.com" });

        var filledSlot = await LoadAsync(filled);
        Assert.Equal(("Sam Substitute", (DateTimeOffset?)null), (filledSlot.AuthorName, filledSlot.DroppedOutAt));
        var openSlot = await LoadAsync(stillOpen);
        Assert.Equal(("data-model-later@example.com", true), (openSlot.Contact?.Email, openSlot.DroppedOutAt is not null));
    }

    [Fact]
    public async Task ASpotWithoutAnAuthorIsntAnyones()
    {
        const int year = 2644;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false);
        await using var db = await fixture.CreateDbContextAsync();
        var contributor = new Contributor { GithubUserId = 264401, GithubLogin = "data-model-nobody", CreatedAt = DateTimeOffset.UtcNow };
        db.Contributors.Add(contributor);
        await db.SaveChangesAsync(CancellationToken);

        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 1 && s.Part == 1, CancellationToken);
        slot.ContributorId = contributor.Id;

        var error = await SaveExpectingErrorAsync(db);
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_slots_contributor_needs_author"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task ASpotWithAnAuthorIsntDroppedOut()
    {
        const int year = 2649;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false);
        await using var db = await fixture.CreateDbContextAsync();
        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 1 && s.Part == 1, CancellationToken);
        slot.AuthorName = "Dee Parted";
        slot.DroppedOutAt = DateTimeOffset.UtcNow;

        var error = await SaveExpectingErrorAsync(db);
        Assert.Equal((PostgresErrorCodes.CheckViolation, "ck_slots_author_not_dropped_out"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task AnAnnouncementsSeasonMustExist()
    {
        await using var db = await fixture.CreateDbContextAsync();
        db.Announcements.Add(SignUpsOpen(2646));

        var error = await SaveExpectingErrorAsync(db);
        Assert.Equal((PostgresErrorCodes.ForeignKeyViolation, "fk_announcements_seasons_season_year"), (error.SqlState, error.ConstraintName));
    }

    [Fact]
    public async Task TheMigrationFixesRowsThatWouldBreakTheRulesFirst()
    {
        // A database at the migration before this one, with rows the code before #188 could leave behind (or that can't
        // exist, but would stop the migration if they did).
        await OnADatabaseOfItsOwnAsync("csadvent_migration_2645", async connectionString =>
        {
            int filled, dropped, linkedWithoutAuthor, linked;
            await using (var db = Create(connectionString))
            {
                await MigrateToBeforeAsync(db, Migration);

                var contributor = new Contributor { GithubUserId = 264501, GithubLogin = "data-model-old", CreatedAt = DateTimeOffset.UtcNow };
                var season = Season.Create(2645, slotsPerDay: 2, DateTimeOffset.UtcNow);
                var droppedAt = DateTimeOffset.UtcNow.AddDays(-2);
                Spot(season, 1).AuthorName = "Given An Author";
                Spot(season, 1).DroppedOutAt = droppedAt;
                Spot(season, 2).DroppedOutAt = droppedAt;
                Spot(season, 3).Contributor = contributor;
                Spot(season, 4).AuthorName = "Linked Author";
                Spot(season, 4).Contributor = contributor;
                db.Seasons.Add(season);
                db.Announcements.AddRange(SignUpsOpen(2645), SignUpsOpen(2647));
                await db.SaveChangesAsync(CancellationToken);
                (filled, dropped, linkedWithoutAuthor, linked) = (Spot(season, 1).Id, Spot(season, 2).Id, Spot(season, 3).Id, Spot(season, 4).Id);

                // Kinds no version ever recorded, which this version can't read.
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO sent_reminders (kind, run_date, slot_id, sent_at) VALUES ('AdminDailySummary', DATE '2645-12-01', NULL, now()), ('ContributorDailyShare', DATE '2645-12-01', {filled}, now())",
                    CancellationToken);

                await db.Database.MigrateAsync(CancellationToken);
            }

            await using (var db = Create(connectionString))
            {
                var slots = await db.Slots.AsNoTracking().Where(s => s.SeasonYear == 2645).ToDictionaryAsync(s => s.Id, CancellationToken);
                Assert.Equal(("Given An Author", (DateTimeOffset?)null), (slots[filled].AuthorName, slots[filled].DroppedOutAt));
                Assert.NotNull(slots[dropped].DroppedOutAt);
                Assert.Null(slots[linkedWithoutAuthor].ContributorId);
                Assert.NotNull(slots[linked].ContributorId);

                // The announcement about a season that isn't there keeps its history, without the season.
                var announcements = await db.Announcements.ToDictionaryAsync(a => a.Subject, a => a.SeasonYear, CancellationToken);
                Assert.Equal(2645, announcements["C# Advent 2645 sign-ups are open"]);
                Assert.Null(announcements["C# Advent 2647 sign-ups are open"]);

                var reminder = await db.SentReminders.SingleAsync(CancellationToken);
                Assert.Equal((ReminderKind.ContributorDailyShare, (int?)filled), (reminder.Kind, reminder.SlotId));

                // And the rules are in place from now on.
                db.Slots.Attach(slots[dropped]).Entity.ContributorId = slots[linked].ContributorId;
                Assert.Equal("ck_slots_contributor_needs_author", (await SaveExpectingErrorAsync(db)).ConstraintName);
                db.ChangeTracker.Clear();
                db.Announcements.Add(SignUpsOpen(2648));
                Assert.Equal("fk_announcements_seasons_season_year", (await SaveExpectingErrorAsync(db)).ConstraintName);
            }
        });
    }

    [Fact]
    public async Task TheDropOutRuleClearsSpotsWithAnAuthorFirst()
    {
        // A database at the migration before the rule, with a spot given an author by the code before #188, which kept
        // serving while TightenDataModel ran, and would come back with a rollback.
        await OnADatabaseOfItsOwnAsync("csadvent_migration_2650", async connectionString =>
        {
            int filled, dropped;
            await using (var db = Create(connectionString))
            {
                await MigrateToBeforeAsync(db, DropOutMigration);

                var season = Season.Create(2650, slotsPerDay: 2, DateTimeOffset.UtcNow);
                var droppedAt = DateTimeOffset.UtcNow.AddDays(-2);
                Spot(season, 1).AuthorName = "Late Taker";
                Spot(season, 1).DroppedOutAt = droppedAt;
                Spot(season, 2).DroppedOutAt = droppedAt;
                db.Seasons.Add(season);
                await db.SaveChangesAsync(CancellationToken);
                (filled, dropped) = (Spot(season, 1).Id, Spot(season, 2).Id);

                await db.Database.MigrateAsync(CancellationToken);
            }

            await using (var db = Create(connectionString))
            {
                var slots = await db.Slots.AsNoTracking().Where(s => s.SeasonYear == 2650).ToDictionaryAsync(s => s.Id, CancellationToken);
                Assert.Equal(("Late Taker", (DateTimeOffset?)null), (slots[filled].AuthorName, slots[filled].DroppedOutAt));
                Assert.Null(slots[dropped].AuthorName);
                Assert.NotNull(slots[dropped].DroppedOutAt);

                // And the rule is in place from now on.
                db.Slots.Attach(slots[dropped]).Entity.AuthorName = "Too Soon";
                Assert.Equal("ck_slots_author_not_dropped_out", (await SaveExpectingErrorAsync(db)).ConstraintName);
            }
        });
    }

    private static Slot Spot(Season season, int day) => season.Slots.Single(s => s.Day == day && s.Part == 1);

    /// <summary>Runs a test on an empty database of its own, dropped afterwards, to migrate it from an earlier migration.</summary>
    private async Task OnADatabaseOfItsOwnAsync(string database, Func<string, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(await fixture.App.GetConnectionStringAsync("csadvent", CancellationToken)) { Database = "postgres" }.ConnectionString;
        await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await ExecuteAsync(server, $"CREATE DATABASE {database}");
        try
        {
            await test(new NpgsqlConnectionStringBuilder(server) { Database = database }.ConnectionString);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    /// <summary>Migrates the database to the migration before <paramref name="migration"/>.</summary>
    private static async Task MigrateToBeforeAsync(AdventDbContext db, string migration)
    {
        var migrations = db.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(m => m.EndsWith($"_{migration}", StringComparison.Ordinal));
        Assert.True(index > 0, $"The {migration} migration isn't there.");
        await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1], cancellationToken: CancellationToken);
    }

    private static Announcement SignUpsOpen(int year) => new()
    {
        Kind = AnnouncementKind.SignUpsOpen,
        SeasonYear = year,
        Subject = $"C# Advent {year} sign-ups are open",
        Message = "A test announcement that's never sent.",
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = "automation",
    };

    private static AdventDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<PostgresException> SaveExpectingErrorAsync(AdventDbContext db)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CancellationToken));
        return Assert.IsType<PostgresException>(exception.InnerException);
    }

    private async Task<int> SlotIdAsync(int year, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private async Task<Slot> LoadAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.AsNoTracking().Include(s => s.Contact).SingleAsync(s => s.Id == slotId, CancellationToken);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
