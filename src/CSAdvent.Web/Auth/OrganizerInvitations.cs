using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Auth;

/// <summary>
/// Turns an organizer invitation by GitHub login into one by GitHub user ID. Organizers invite people by login, before
/// they've signed in, but a login can be renamed and the old one registered by someone else. So when someone with that login
/// next signs in with GitHub, the invitation becomes theirs by user ID, and the login no longer counts.
/// </summary>
/// <remarks>
/// Unlike a test season invitation (<see cref="SeasonTesting.TesterInvitations"/>), it's only claimed at sign-in, when the
/// login has just come from GitHub, never from the login in an older sign-in cookie, which could be one its owner has since
/// given up. Someone already signed in when they're invited signs in again.
/// </remarks>
public sealed class OrganizerInvitations(AdventDbContext db, Organizers organizers, ILogger<OrganizerInvitations> logger)
{
    /// <summary>
    /// Gives the GitHub user the invitation waiting for their login. An invitation for someone who's already an organizer (by
    /// an earlier login) isn't needed, so it's deleted. Never throws for a clash with another request doing the same, because
    /// it runs during sign-in.
    /// </summary>
    public async Task ClaimAsync(long githubUserId, string login, CancellationToken cancellationToken = default)
    {
        var invitedAs = login.Trim().ToLowerInvariant();
        try
        {
            var duplicates = await db.Organizers
                .Where(o => o.GithubUserId == null && o.GithubLogin == invitedAs && db.Organizers.Any(other => other.GithubUserId == githubUserId))
                .ExecuteDeleteAsync(cancellationToken);

            var claimed = await db.Organizers
                .Where(o => o.GithubUserId == null && o.GithubLogin == invitedAs && !db.Organizers.Any(other => other.GithubUserId == githubUserId))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.GithubUserId, githubUserId), cancellationToken);

            if (duplicates + claimed > 0)
            {
                logger.LogInformation("An organizer invitation is now an organizer's by GitHub user ID.");
                await organizers.RefreshAsync(cancellationToken);
            }
        }
        catch (PostgresException exception) when (exception.IsUniqueViolation())
        {
            // Two sign-ins claimed it at once: the other one did it.
            await organizers.RefreshAsync(cancellationToken);
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            // A revision started without its migrations (an image changed by hand, #225) has no table yet. Signing in carries
            // on, and the invitation becomes theirs the next time they sign in.
            logger.LogWarning("Organizer invitations can't be claimed until the database is migrated.");
        }
    }
}
