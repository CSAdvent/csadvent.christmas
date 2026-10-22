using CSAdvent.Web.Auth;
using CSAdvent.Web.Claims;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Social;

/// <summary>What the "Mark as posted" and "Undo" buttons on the social page post. Plain forms, because the page has one per entry and platform.</summary>
public sealed class SocialMarkForm
{
    public int SlotId { get; set; }

    public SocialPlatform Platform { get; set; }

    public string? PostUrl { get; set; }

    /// <summary>The social page to go back to, such as <c>manage/social?year=2026&amp;day=6</c>.</summary>
    public string? ReturnUrl { get; set; }
}

public static class SocialEndpoints
{
    public static WebApplication MapSocial(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        var group = app.MapGroup("/manage/social").RequireAuthorization(AdventPolicies.Admin);

        group.MapPost("/mark", async ([FromForm] SocialMarkForm form, HttpContext context, SocialService social, CancellationToken cancellationToken) =>
        {
            var postUrl = new HttpUrlAttribute().IsValid(form.PostUrl) && !string.IsNullOrWhiteSpace(form.PostUrl) ? form.PostUrl.Trim() : null;
            var outcome = await social.MarkPostedAsync(form.SlotId, form.Platform, postUrl, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(Back(form, outcome is MarkOutcome.NotRevealed ? "not-revealed" : null));
        });

        group.MapPost("/unmark", async ([FromForm] SocialMarkForm form, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.UnmarkAsync(form.SlotId, form.Platform, cancellationToken);
            return Results.LocalRedirect(Back(form, null));
        });

        return app;
    }

    // Only back to the social page itself, so the form can't be used to redirect anywhere else.
    private static string Back(SocialMarkForm form, string? error)
    {
        var target = form.ReturnUrl is { } url && url.StartsWith("manage/social", StringComparison.Ordinal) && !url.Contains("//", StringComparison.Ordinal)
            ? $"/{url}"
            : "/manage/social";
        return error is null ? target : $"{target}{(target.Contains('?', StringComparison.Ordinal) ? '&' : '?')}error={error}";
    }
}
