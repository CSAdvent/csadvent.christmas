using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Auth;

public static class AdventPolicies
{
    /// <summary>Organizers: signed-in users whose GitHub user ID is in <see cref="AccountOptions.Admins"/>.</summary>
    public const string Admin = "Admin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>Checks the configured organizers on every request, so changing the list never needs anyone to sign in again.</summary>
public sealed class AdminAuthorizationHandler(IOptionsMonitor<AccountOptions> options) : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.GetGitHubUserId() is { } id && options.CurrentValue.Admins.Contains(id))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
