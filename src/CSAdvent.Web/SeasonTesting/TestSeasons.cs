using System.Collections.Frozen;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// The test season: the date standing in for December 1, and who's invited. <paramref name="TesterIds"/> are the GitHub user
/// IDs of the testers whose invitation has become theirs; <paramref name="InvitedLogins"/> are the logins (any case) of the
/// invitations still waiting for someone to use them (<see cref="TesterInvitations"/>).
/// </summary>
public sealed record TestSeason(int Year, DateOnly RehearsalStartsOn, IReadOnlySet<long> TesterIds, IReadOnlySet<string> InvitedLogins)
{
    /// <summary>Whether this GitHub user is a tester: by user ID, or by login while their invitation is still waiting.</summary>
    public bool IsTester(long? githubUserId, string? login) =>
        (githubUserId is { } id && TesterIds.Contains(id)) || (login is not null && InvitedLogins.Contains(login));
}

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

    /// <summary>Whether an invitation to the test season is waiting for whoever has this GitHub login.</summary>
    public bool IsInvitationWaiting(string login) => Current?.InvitedLogins.Contains(login) == true;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();
        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year == Season.TestYear && s.RehearsalStartsOn != null)
            .Select(s => new
            {
                s.Year,
                s.RehearsalStartsOn,
                TesterIds = s.Testers.Where(t => t.GithubUserId != null).Select(t => t.GithubUserId!.Value).ToList(),
                InvitedLogins = s.Testers.Where(t => t.GithubUserId == null).Select(t => t.GithubLogin).ToList(),
            })
            .ToListAsync(cancellationToken);

        Replace(seasons.Select(s => new TestSeason(
            s.Year,
            s.RehearsalStartsOn!.Value,
            s.TesterIds.ToFrozenSet(),
            s.InvitedLogins.ToFrozenSet(StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>Sets the test season. <see cref="RefreshAsync"/> uses it, and so do tests.</summary>
    public void Replace(IEnumerable<TestSeason> seasons) => _seasons = seasons.ToFrozenDictionary(s => s.Year);
}
