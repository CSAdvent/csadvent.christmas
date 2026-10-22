using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>What a tester's Remove button posts. A plain form, because there's one per tester.</summary>
public sealed class TesterRemoveForm
{
    public int Year { get; set; }

    public string Login { get; set; } = "";
}

public static class SeasonTestingEndpoints
{
    public static WebApplication MapSeasonTesting(this WebApplication app)
    {
        // Binding a form makes this check the antiforgery token.
        app.MapPost("/manage/testing/testers/remove", async ([FromForm] TesterRemoveForm form, SeasonTestingService testing, CancellationToken cancellationToken) =>
            {
                await testing.RemoveTesterAsync(form.Year, form.Login, cancellationToken);
                return Results.LocalRedirect($"/manage/testing?year={form.Year}&done=removed");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }
}
