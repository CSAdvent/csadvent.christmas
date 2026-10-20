using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>The 2017-2025 history is seeded by the AppHost's migrations step, before the web app starts.</summary>
public sealed class ArchiveTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryArchivedSeasonIsSeededAfterMigrations()
    {
        await using var db = await fixture.CreateDbContextAsync();

        var posts = await db.Slots
            .Where(s => s.SeasonYear >= 2017 && s.SeasonYear <= 2025)
            .GroupBy(s => s.SeasonYear)
            .Select(g => new { Year = g.Key, Slots = g.Count(), Posts = g.Count(s => s.PostUrl != null) })
            .OrderBy(g => g.Year)
            .ToListAsync(CancellationToken);

        Assert.Equal(Enumerable.Range(2017, 9), posts.Select(p => p.Year));
        Assert.Equal(25, posts[0].Slots);
        Assert.All(posts.Skip(1), p => Assert.Equal(50, p.Slots));
        Assert.Equal(408, posts.Sum(p => p.Posts));
    }

    [Fact]
    public async Task SeedingAgainAddsNothing()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var before = await db.Slots.CountAsync(s => s.SeasonYear <= 2025, CancellationToken);

        ArchiveSeeder.Seed(db);
        await ArchiveSeeder.SeedAsync(db, CancellationToken);

        Assert.Equal(before, await db.Slots.CountAsync(s => s.SeasonYear <= 2025, CancellationToken));
    }
}
