using Microsoft.AspNetCore.Authorization;

namespace CSAdvent.Web.Auth;

public static class AdventPolicies
{
    /// <summary>Organizers: signed-in users who are one of the <see cref="Organizers"/>.</summary>
    public const string Admin = "Admin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Checks the organizers on every request, so inviting or removing one never needs anyone else to sign in again. The list is
/// in memory (<see cref="Organizers"/>), so it's only a database query on a replica that hasn't loaded it yet.
/// </summary>
public sealed class AdminAuthorizationHandler(Organizers organizers) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.GetGitHubUserId() is not { } id)
        {
            return;
        }

        if (!organizers.IsDefault(id))
        {
            await organizers.EnsureLoadedAsync();
        }

        if (organizers.IsOrganizer(id))
        {
            context.Succeed(requirement);
        }
    }
}
