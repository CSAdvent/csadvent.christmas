using System.Security.Claims;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// Turns a tester invitation by GitHub login into one by GitHub user ID (#192). Organizers invite people by login, before
/// they've signed in, but a login can be renamed and the old one registered by someone else. So the first time someone with
/// that login signs in, or visits with a sign-in from before they were invited, the invitation becomes theirs by user ID,
/// and the login no longer counts. Invitations from before #192 were all logins, and become IDs the same way.
/// </summary>
public sealed class TesterInvitations(AdventDbContext db, TestSeasons testSeasons, ILogger<TesterInvitations> logger)
{
    /// <summary>
    /// Gives the GitHub user the invitations waiting for their login. An invitation for someone who's already a tester (by an
    /// earlier login) isn't needed, so it's deleted. Never throws for a clash with another request doing the same, because it
    /// runs during sign-in.
    /// </summary>
    public async Task ClaimAsync(long githubUserId, string login, CancellationToken cancellationToken = default)
    {
        var invitedAs = login.Trim().ToLowerInvariant();
        try
        {
            var duplicates = await db.SeasonTesters
                .Where(t => t.GithubUserId == null && t.GithubLogin == invitedAs
                    && db.SeasonTesters.Any(other => other.SeasonYear == t.SeasonYear && other.GithubUserId == githubUserId))
                .ExecuteDeleteAsync(cancellationToken);

            var claimed = await db.SeasonTesters
                .Where(t => t.GithubUserId == null && t.GithubLogin == invitedAs
                    && !db.SeasonTesters.Any(other => other.SeasonYear == t.SeasonYear && other.GithubUserId == githubUserId))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GithubUserId, githubUserId), cancellationToken);

            if (duplicates + claimed > 0)
            {
                logger.LogInformation("A test season invitation is now a tester's by GitHub user ID.");
                await testSeasons.RefreshAsync(cancellationToken);
            }
        }
        catch (PostgresException exception) when (exception.IsUniqueViolation())
        {
            // Two requests claimed it at once: the other one did it.
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            // A revision started without its migrations (an image changed by hand, #225) has no column yet. Signing in
            // carries on, and the invitation becomes theirs on a later visit.
            logger.LogWarning("Test season invitations can't be claimed until the database is migrated.");
        }
    }

    /// <summary>
    /// For the sign-in cookie's check on every request: claims a waiting invitation for someone who was already signed in
    /// when they were invited. It's an in-memory lookup unless an invitation is waiting for this login.
    /// </summary>
    public static async Task ClaimForSignedInUserAsync(HttpContext context, ClaimsPrincipal user)
    {
        if (user.GetGitHubUserId() is not { } githubUserId || user.Identity?.Name is not { Length: > 0 } login)
        {
            return;
        }

        if (context.RequestServices.GetRequiredService<TestSeasons>().IsInvitationWaiting(login))
        {
            await context.RequestServices.GetRequiredService<TesterInvitations>().ClaimAsync(githubUserId, login, context.RequestAborted);
        }
    }
}
