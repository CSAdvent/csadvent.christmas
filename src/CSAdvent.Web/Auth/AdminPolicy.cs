using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Auth;

public static class AdventPolicies
{
    /// <summary>Organizers: signed-in users whose GitHub user ID is in <see cref="AccountOptions.Admins"/>.</summary>
    public const string Admin = "Admin";

    public static bool IsOrganizer(ClaimsPrincipal user, AccountOptions options) =>
        user.GetGitHubUserId() is { } id && options.Admins.Contains(id);
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>Checks the configured organizers on every request, so changing the list never needs anyone to sign in again.</summary>
public sealed class AdminAuthorizationHandler(IOptionsMonitor<AccountOptions> options) : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (AdventPolicies.IsOrganizer(context.User, options.CurrentValue))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
