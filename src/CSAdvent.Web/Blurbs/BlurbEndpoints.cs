using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Blurbs;

/// <summary>What the blurb buttons post. Plain forms, because the pages have one per post.</summary>
public sealed class BlurbForm
{
    public int SlotId { get; set; }

    public BlurbDecision Decision { get; set; }

    /// <summary>The organizer page to go back to, such as <c>manage/entries/42</c>.</summary>
    public string? ReturnUrl { get; set; }
}

public static class BlurbEndpoints
{
    public static WebApplication MapBlurbs(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        var group = app.MapGroup("/manage/blurbs").RequireAuthorization(AdventPolicies.Admin);

        group.MapPost("/check", async ([FromForm] BlurbForm form, HttpContext context, BlurbCheckService blurbs, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            await blurbs.CheckNowAsync(form.SlotId, Organizer(context), clock.GetUtcNow(), cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, form.SlotId, "checked"));
        });

        group.MapPost("/decide", async ([FromForm] BlurbForm form, HttpContext context, BlurbCheckService blurbs, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            await blurbs.DecideAsync(form.SlotId, form.Decision, Organizer(context), clock.GetUtcNow(), cancellationToken: cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, form.SlotId, "decided"));
        });

        group.MapPost("/backup", async ([FromForm] BlurbForm form, HttpContext context, BlurbCheckService blurbs, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            await blurbs.UseBackupAsync(form.SlotId, Organizer(context), clock.GetUtcNow(), cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, form.SlotId, "backup"));
        });

        if (app.Environment.IsDevelopment())
        {
            // Development only: runs the checks as if it were the given time. With dry-run mode and the sample pages, nothing
            // reaches Anthropic or the internet.
            app.MapPost("/dev/blurbs/run", async (DateTimeOffset at, int? year, BlurbCheckService blurbs, CancellationToken cancellationToken) =>
                Results.Ok(await blurbs.RunAsync(at.ToUniversalTime(), year, cancellationToken)));
        }

        return app;
    }

    private static string Organizer(HttpContext context) => context.User.Identity?.Name ?? "organizer";

    /// <summary>Back to the organizer page the button was on (only those), or the post's entry page.</summary>
    private static string Back(string? returnUrl, int slotId, string? done)
    {
        var target = returnUrl is { } url
            && (url.StartsWith("manage/entries", StringComparison.Ordinal) || url.StartsWith("manage/social", StringComparison.Ordinal))
            && !url.Contains("//", StringComparison.Ordinal)
            ? $"/{url}"
            : $"/manage/entries/{slotId}";
        return done is null ? target : $"{target}{(target.Contains('?', StringComparison.Ordinal) ? '&' : '?')}blurb={done}";
    }
}
