using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Content;

/// <summary>What a Remove button on the Other Advents admin page posts. A plain form, because there's one per calendar.</summary>
public sealed class OtherAdventRemoveForm
{
    public int Year { get; set; }

    public int Id { get; set; }
}

public static class ContentAdminEndpoints
{
    public static WebApplication MapContentAdmin(this WebApplication app)
    {
        // Binding a form makes this check the antiforgery token.
        app.MapPost("/manage/other-advents/remove", async ([FromForm] OtherAdventRemoveForm form, OtherAdventsService others, CancellationToken cancellationToken) =>
            {
                await others.RemoveAsync(form.Year, form.Id, cancellationToken);
                return Results.LocalRedirect($"/manage/other-advents?year={form.Year}");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }
}
