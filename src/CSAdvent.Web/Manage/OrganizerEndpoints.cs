using System.Security.Claims;
using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Manage;

public static class OrganizerEndpoints
{
    public static WebApplication MapOrganizers(this WebApplication app)
    {
        // Binding a form makes this check the antiforgery token.
        app.MapPost("/manage/organizers/remove", async ([FromForm] OrganizerRemoveForm form, ClaimsPrincipal user, OrganizerService organizers, CancellationToken cancellationToken) =>
            {
                var outcome = await organizers.RemoveAsync(form.Id, user.GetGitHubUserId()!.Value, cancellationToken);
                return Results.LocalRedirect(outcome switch
                {
                    OrganizerRemoveOutcome.Removed => "/manage/organizers?done=removed",
                    OrganizerRemoveOutcome.Yourself => "/manage/organizers?done=yourself",
                    _ => "/manage/organizers?done=gone",
                });
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }
}
