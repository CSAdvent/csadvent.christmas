using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Auth;

/// <summary>Records a GitHub sign-in as a contributor, creating the row the first time and refreshing it after that.</summary>
public sealed class ContributorSignIn(AdventDbContext db, TimeProvider timeProvider)
{
    /// <summary>
    /// Inserts or updates the contributor in one statement, so two sign-ins at once can't create duplicates. Returns the
    /// contributor's ID. The verified email comes from GitHub and replaces any earlier one.
    /// </summary>
    public async Task<int> RecordAsync(long githubUserId, string login, string? verifiedEmail, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        DateTimeOffset? verifiedAt = verifiedEmail is null ? null : now;

        var ids = await db.Database.SqlQuery<int>($"""
            INSERT INTO contributors (github_user_id, github_login, email, email_verified_at, created_at)
            VALUES ({githubUserId}, {login}, {verifiedEmail}, {verifiedAt}, {now})
            ON CONFLICT (github_user_id) DO UPDATE
                SET github_login = excluded.github_login,
                    email = excluded.email,
                    email_verified_at = excluded.email_verified_at
            RETURNING id AS "Value"
            """).ToListAsync(cancellationToken);

        return ids.Single();
    }
}
