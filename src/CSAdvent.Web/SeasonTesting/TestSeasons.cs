using System.Collections.Frozen;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>The test season: the date standing in for December 1, and who's invited (GitHub logins, any case).</summary>
public sealed record TestSeason(int Year, DateOnly RehearsalStartsOn, IReadOnlySet<string> Testers);

/// <summary>
/// The test season (<see cref="Season.TestYear"/>), kept in memory because the schedule and every page ask.
/// <see cref="TestSeasonsRefresher"/> reloads it every few seconds, and organizer changes reload it straight away, so every
/// replica catches up quickly.
/// </summary>
public sealed class TestSeasons(IServiceScopeFactory scopeFactory)
{
    private volatile FrozenDictionary<int, TestSeason> _seasons = FrozenDictionary<int, TestSeason>.Empty;

    /// <summary>The test season, once an organizer has created it.</summary>
    public TestSeason? Current => _seasons.GetValueOrDefault(Season.TestYear);

    public TestSeason? Find(int year) => _seasons.GetValueOrDefault(year);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();
        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year == Season.TestYear && s.RehearsalStartsOn != null)
            .Select(s => new { s.Year, s.RehearsalStartsOn, Testers = s.Testers.Select(t => t.GithubLogin).ToList() })
            .ToListAsync(cancellationToken);

        Replace(seasons.Select(s => new TestSeason(s.Year, s.RehearsalStartsOn!.Value, s.Testers.ToFrozenSet(StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>Sets the test season. <see cref="RefreshAsync"/> uses it, and so do tests.</summary>
    public void Replace(IEnumerable<TestSeason> seasons) => _seasons = seasons.ToFrozenDictionary(s => s.Year);
}
