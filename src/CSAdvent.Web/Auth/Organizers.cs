using System.Collections.Frozen;
using System.Security.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Auth;

/// <summary>
/// Who the organizers are, by GitHub user ID: the default ones in <see cref="AccountOptions.Admins"/>, who always are, and
/// those invited on <c>/manage/organizers</c> whose invitation has become theirs (<see cref="Organizer"/>). Kept in memory
/// because the sign-in cookie asks on every request. <see cref="OrganizersRefresher"/> reloads it every few seconds, and
/// changes reload it straight away, so every replica catches up quickly.
/// </summary>
public sealed class Organizers(IServiceScopeFactory scopeFactory, IOptionsMonitor<AccountOptions> accounts)
{
    // Null until the first load.
    private volatile FrozenSet<long>? _invited;

    /// <summary>Whether the invited organizers have been loaded since the app started.</summary>
    public bool IsLoaded => _invited is not null;

    /// <summary>The default organizers' GitHub user IDs, from configuration. They can't be removed on the organizers page.</summary>
    public IReadOnlyList<long> Defaults => accounts.CurrentValue.Admins;

    public bool IsOrganizer(ClaimsPrincipal user) => user.GetGitHubUserId() is { } id && IsOrganizer(id);

    public bool IsOrganizer(long githubUserId) => IsDefault(githubUserId) || _invited?.Contains(githubUserId) == true;

    public bool IsDefault(long githubUserId) => accounts.CurrentValue.Admins.Contains(githubUserId);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();
        var invited = await db.Organizers
            .AsNoTracking()
            .Where(o => o.GithubUserId != null)
            .Select(o => o.GithubUserId!.Value)
            .ToListAsync(cancellationToken);

        Replace(invited);
    }

    /// <summary>
    /// Loads the invited organizers if a replica that has only just started hasn't yet, so they aren't turned away for the
    /// moment before <see cref="OrganizersRefresher"/>'s first run finishes.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTable(exception))
        {
            // A revision started without its migrations (#225): nobody can have been invited before the table existed.
            // OrganizersRefresher keeps trying, and loads them once it's there.
            Replace([]);
        }
    }

    /// <summary>Sets the invited organizers' GitHub user IDs. <see cref="RefreshAsync"/> uses it, and so do tests.</summary>
    public void Replace(IEnumerable<long> invited) => _invited = invited.ToFrozenSet();
}
