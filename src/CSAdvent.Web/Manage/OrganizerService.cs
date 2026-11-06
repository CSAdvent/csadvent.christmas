using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Manage;

/// <summary>A default organizer, from <c>Accounts:Admins</c>: their GitHub login if they've signed in since it was set.</summary>
public sealed record DefaultOrganizerView(long GithubUserId, string? Login);

/// <summary>
/// An invited organizer: the login they were invited as, and once their invitation is theirs (by GitHub user ID), their
/// GitHub login as of their last sign-in.
/// </summary>
public sealed record InvitedOrganizerView(int Id, string InvitedAs, long? GithubUserId, string? Login, DateTimeOffset InvitedAt, string? InvitedBy)
{
    /// <summary>Whether they've signed in since they were invited. Until then it's only a login, and gives nobody anything.</summary>
    public bool IsWaiting => GithubUserId is null;

    /// <summary>The login to show and link to.</summary>
    public string CurrentLogin => Login ?? InvitedAs;
}

public sealed record OrganizersView(IReadOnlyList<DefaultOrganizerView> Defaults, IReadOnlyList<InvitedOrganizerView> Invited);

public enum OrganizerInviteOutcome
{
    Invited,
    AlreadyAnOrganizer,
    NotALogin,
}

public enum OrganizerRemoveOutcome
{
    Removed,

    /// <summary>Nobody's invitation has that ID: someone else removed it first.</summary>
    NotFound,

    /// <summary>Organizers can't remove themselves, so nobody locks themselves out by mistake.</summary>
    Yourself,
}

/// <summary>
/// The organizers page: the default organizers, who are set in configuration (<see cref="AccountOptions.Admins"/>) and can't
/// be removed here, and the ones invited by GitHub login, who can. An invitation counts once someone with that login signs
/// in with GitHub (<see cref="OrganizerInvitations"/>).
/// </summary>
public sealed class OrganizerService(AdventDbContext db, Organizers organizers, TimeProvider timeProvider, ILogger<OrganizerService> logger)
{
    public async Task<OrganizersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var defaults = organizers.Defaults.ToList();
        var defaultLogins = await db.Contributors
            .AsNoTracking()
            .Where(c => defaults.Contains(c.GithubUserId))
            .ToDictionaryAsync(c => c.GithubUserId, c => c.GithubLogin, cancellationToken);

        var invited = await db.Organizers
            .AsNoTracking()
            .Select(o => new InvitedOrganizerView(
                o.Id,
                o.GithubLogin,
                o.GithubUserId,
                o.GithubUserId == null ? null : db.Contributors.Where(c => c.GithubUserId == o.GithubUserId).Select(c => c.GithubLogin).FirstOrDefault(),
                o.InvitedAt,
                o.InvitedByGithubUserId == null ? null : db.Contributors.Where(c => c.GithubUserId == o.InvitedByGithubUserId).Select(c => c.GithubLogin).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new OrganizersView(
            [.. defaults.Select(id => new DefaultOrganizerView(id, defaultLogins.GetValueOrDefault(id)))],
            [.. invited.OrderBy(o => o.CurrentLogin, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Invites an organizer by GitHub login (or @login, or their GitHub profile URL). It counts once someone with that login
    /// signs in with GitHub. Nothing changes if that login is already invited, or is the current login of someone who's
    /// already an organizer.
    /// </summary>
    public async Task<OrganizerInviteOutcome> InviteAsync(string login, long invitedBy, CancellationToken cancellationToken = default)
    {
        if (GitHubLogins.Normalize(login) is not { } normalized)
        {
            return OrganizerInviteOutcome.NotALogin;
        }

        var defaults = organizers.Defaults.ToList();
        var already = await db.Organizers.AnyAsync(
                o => o.GithubLogin == normalized
                    || (o.GithubUserId != null && db.Contributors.Any(c => c.GithubUserId == o.GithubUserId && c.GithubLogin.ToLower() == normalized)),
                cancellationToken)
            || await db.Contributors.AnyAsync(c => defaults.Contains(c.GithubUserId) && c.GithubLogin.ToLower() == normalized, cancellationToken);
        if (already)
        {
            return OrganizerInviteOutcome.AlreadyAnOrganizer;
        }

        db.Organizers.Add(new Organizer { GithubLogin = normalized, InvitedAt = timeProvider.GetUtcNow(), InvitedByGithubUserId = invitedBy });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another organizer invited the same login at the same moment.
            return OrganizerInviteOutcome.AlreadyAnOrganizer;
        }

        logger.LogInformation("An organizer was invited.");
        await organizers.RefreshAsync(cancellationToken);
        return OrganizerInviteOutcome.Invited;
    }

    /// <summary>Removes an invited organizer, or an invitation still waiting. The default organizers can't be removed here.</summary>
    public async Task<OrganizerRemoveOutcome> RemoveAsync(int id, long removedBy, CancellationToken cancellationToken = default)
    {
        if (await db.Organizers.AnyAsync(o => o.Id == id && o.GithubUserId == removedBy, cancellationToken))
        {
            return OrganizerRemoveOutcome.Yourself;
        }

        // Never the remover's own, even if their invitation became theirs since the check above.
        var removed = await db.Organizers.Where(o => o.Id == id && o.GithubUserId != removedBy).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            return OrganizerRemoveOutcome.NotFound;
        }

        logger.LogInformation("An organizer was removed.");
        await organizers.RefreshAsync(cancellationToken);
        return OrganizerRemoveOutcome.Removed;
    }
}

public sealed class OrganizerInviteForm
{
    [Required(ErrorMessage = "Enter a GitHub login.")]
    [StringLength(100)]
    public string Login { get; set; } = "";
}

/// <summary>What an organizer's Remove button posts. A plain form, because there's one per organizer.</summary>
public sealed class OrganizerRemoveForm
{
    public int Id { get; set; }
}
