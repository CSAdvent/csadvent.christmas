using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Runs against the database the AppHost migrated. Each test uses its own season year, so tests can't interfere.
/// </summary>
public sealed class DatabaseTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryMigrationIsApplied()
    {
        await using var db = await fixture.CreateDbContextAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(CancellationToken));
    }

    [Fact]
    public async Task CreatingASeasonLeavesEarlierSeasonsUntouched()
    {
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var earlier = Season.Create(2090, slotsPerDay: 2, Now);
            var slot = earlier.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Ada";
            slot.PostTitle = "Hello, Advent";
            slot.PostUrl = "https://example.com/hello";
            db.Seasons.Add(earlier);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            db.Seasons.Add(Season.Create(2091, slotsPerDay: 2, Now));
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var earlierSlots = await db.Slots.Where(s => s.SeasonYear == 2090).ToListAsync(CancellationToken);

            Assert.Equal(50, earlierSlots.Count);
            var claimed = Assert.Single(earlierSlots, s => s.IsClaimed);
            Assert.Equal(("Ada", "Hello, Advent", "https://example.com/hello"), (claimed.AuthorName, claimed.PostTitle, claimed.PostUrl));
            Assert.Equal(50, await db.Slots.CountAsync(s => s.SeasonYear == 2091, CancellationToken));
        }
    }

    [Fact]
    public async Task ASeasonCantHaveTheSameDayAndPartTwice()
    {
        var error = await SaveSeasonExpectingErrorAsync(2092, season =>
            season.Slots.Add(new Slot { SeasonYear = season.Year, Day = 1, Part = 1 }));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ix_slots_season_year_day_part", error.ConstraintName);
    }

    [Fact]
    public async Task SlotsRejectDaysOutsideAdvent()
    {
        var error = await SaveSeasonExpectingErrorAsync(2093, season =>
            season.Slots.Add(new Slot { SeasonYear = season.Year, Day = 26, Part = 1 }));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_slots_day", error.ConstraintName);
    }

    [Fact]
    public async Task SlotsRejectLinksThatArentHttp()
    {
        var error = await SaveSeasonExpectingErrorAsync(2094, season =>
        {
            var slot = season.Slots[0];
            slot.AuthorName = "Grace";
            slot.PostUrl = "ftp://example.com/post";
        });

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_slots_post_url", error.ConstraintName);
    }

    [Fact]
    public async Task PurgingContactDetailsLeavesThePublicCalendarIntact()
    {
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = Season.Create(2095, slotsPerDay: 2, Now);
            var slot = season.Slots[0];
            slot.AuthorName = "Linus";
            slot.Contact = new SlotContact { Email = "linus@example.com" };
            db.Seasons.Add(season);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            await db.SlotContacts.Where(c => c.Slot.SeasonYear == 2095).ExecuteDeleteAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == 2095 && s.AuthorName != null, CancellationToken);

            Assert.Equal("Linus", slot.AuthorName);
            Assert.Null(slot.Contact);
        }
    }

    private async Task<PostgresException> SaveSeasonExpectingErrorAsync(int year, Action<Season> change)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var season = Season.Create(year, slotsPerDay: 1, Now);
        change(season);
        db.Seasons.Add(season);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CancellationToken));
        return Assert.IsType<PostgresException>(exception.InnerException);
    }
}
