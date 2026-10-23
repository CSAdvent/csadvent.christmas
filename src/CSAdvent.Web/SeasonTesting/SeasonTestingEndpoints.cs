using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>What a tester's Remove button posts. A plain form, because there's one per tester.</summary>
public sealed class TesterRemoveForm
{
    public string Login { get; set; } = "";
}

public static class SeasonTestingEndpoints
{
    public static WebApplication MapSeasonTesting(this WebApplication app)
    {
        // Binding a form makes this check the antiforgery token.
        app.MapPost("/manage/testing/testers/remove", async ([FromForm] TesterRemoveForm form, SeasonTestingService testing, CancellationToken cancellationToken) =>
            {
                await testing.RemoveTesterAsync(form.Login, cancellationToken);
                return Results.LocalRedirect("/manage/testing?done=removed");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        // Test mode only changes what this browser's home page and archive show, so a plain link can switch it. Anyone who
        // isn't a tester is just sent on, and gets nothing.
        app.MapGet("/test-mode/on", (string? returnUrl, SeasonAccess access, HttpContext context) =>
        {
            if (access.TestSeason is not null)
            {
                context.Response.Cookies.Append(SeasonAccess.TestModeCookie, "on", new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                });
            }

            return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
        });

        app.MapGet("/test-mode/off", (string? returnUrl, HttpContext context) =>
        {
            context.Response.Cookies.Delete(SeasonAccess.TestModeCookie);
            return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
        });

        return app;
    }
}
