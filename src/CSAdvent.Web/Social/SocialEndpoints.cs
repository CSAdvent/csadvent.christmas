using CSAdvent.Web.Auth;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social.Posting;
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

/// <summary>What the season posts' buttons post (<c>/manage/social/signups</c>): one per platform and kind, so plain forms.</summary>
public sealed class SeasonPostForm
{
    public int Year { get; set; }

    public SeasonPostKind Kind { get; set; }

    public SocialPlatform Platform { get; set; }

    public string? PostUrl { get; set; }

    /// <summary>For Undo: the post to forget.</summary>
    public int PostId { get; set; }
}

/// <summary>What a platform's mode and Check buttons on the social page post. Plain forms, because there's one per platform.</summary>
public sealed class SocialPlatformForm
{
    public SocialPlatform Platform { get; set; }

    public SocialPostingMode Mode { get; set; }

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
            return Results.LocalRedirect(Back(form.ReturnUrl, outcome is MarkOutcome.NotRevealed ? "not-revealed" : null));
        });

        group.MapPost("/unmark", async ([FromForm] SocialMarkForm form, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.UnmarkAsync(form.SlotId, form.Platform, cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, null));
        });

        // Automatic posting (#61): retry a failed post now, change a platform's mode, or check its credentials.
        group.MapPost("/retry", async ([FromForm] SocialMarkForm form, SocialPostingService posting, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            var outcome = await posting.RetryAsync(form.SlotId, form.Platform, timeProvider.GetUtcNow(), cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, outcome is PostOutcome.Failed ? "retry-failed" : null));
        });

        group.MapPost("/mode", async ([FromForm] SocialPlatformForm form, HttpContext context, SocialPlatformModes modes, CancellationToken cancellationToken) =>
        {
            var changed = await modes.SetAsync(form.Platform, form.Mode, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, changed ? null : "no-credentials"));
        });

        group.MapPost("/check", async ([FromForm] SocialPlatformForm form, HttpContext context, SocialPlatformModes modes, CancellationToken cancellationToken) =>
        {
            await modes.CheckAsync(form.Platform, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, null));
        });

        // A real, public post of the latest entry's announcement, so organizers can see it as followers will.
        group.MapPost("/test", async ([FromForm] SocialPlatformForm form, HttpContext context, SocialPostingService posting, CancellationToken cancellationToken) =>
        {
            var error = await posting.SendTestAsync(form.Platform, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(Back(form.ReturnUrl, error is null ? null : "test-failed"));
        });

        // The season posts: "sign-ups open soon" and "sign-ups are open".
        group.MapPost("/signups/post", async ([FromForm] SeasonPostForm form, HttpContext context, SeasonSocialService season, CancellationToken cancellationToken) =>
        {
            var outcome = await season.PostAsync(form.Year, form.Kind, form.Platform, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(SignUpsPage(form.Year, outcome is null or PostOutcome.Failed ? "not-posted" : null));
        });

        group.MapPost("/signups/mark", async ([FromForm] SeasonPostForm form, HttpContext context, SeasonSocialService season, CancellationToken cancellationToken) =>
        {
            var postUrl = new HttpUrlAttribute().IsValid(form.PostUrl) && !string.IsNullOrWhiteSpace(form.PostUrl) ? form.PostUrl.Trim() : null;
            await season.MarkAsync(form.Year, form.Kind, form.Platform, postUrl, context.User.Identity?.Name ?? "organizer", cancellationToken);
            return Results.LocalRedirect(SignUpsPage(form.Year, null));
        });

        group.MapPost("/signups/unmark", async ([FromForm] SeasonPostForm form, SeasonSocialService season, CancellationToken cancellationToken) =>
        {
            await season.UnmarkAsync(form.PostId, cancellationToken);
            return Results.LocalRedirect(SignUpsPage(form.Year, null));
        });

        // Development only: run automatic posting as if it were the given time. With dry-run mode, nothing reaches a real account.
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/dev/social/run", async (DateTimeOffset at, SocialPostingService posting, CancellationToken cancellationToken) =>
                Results.Ok(await posting.RunAsync(at.ToUniversalTime(), cancellationToken)));
            app.MapPost("/dev/social/signups/run", async (DateTimeOffset at, int? year, SeasonSocialService season, CancellationToken cancellationToken) =>
                Results.Ok(new { posted = await season.RunAsync(at.ToUniversalTime(), year, cancellationToken) }));
        }

        return app;
    }

    private static string SignUpsPage(int year, string? error) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/manage/social/signups?year={year}{(error is null ? "" : $"&error={error}")}");

    // Only back to the social page itself, so the form can't be used to redirect anywhere else.
    private static string Back(string? returnUrl, string? error)
    {
        var target = returnUrl is { } url && url.StartsWith("manage/social", StringComparison.Ordinal) && !url.Contains("//", StringComparison.Ordinal)
            ? $"/{url}"
            : "/manage/social";
        return error is null ? target : $"{target}{(target.Contains('?', StringComparison.Ordinal) ? '&' : '?')}error={error}";
    }
}
