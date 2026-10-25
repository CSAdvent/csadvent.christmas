using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Announcements;

public static class AnnouncementEndpoints
{
    public const string RateLimitPolicy = "announcements-subscribe";
    public const string Page = "/announcements";
    public const string ConfirmPage = "/announcements/confirm";
    public const string UnsubscribePage = "/announcements/unsubscribe";
    public const string DailyPostsUnsubscribePage = "/announcements/daily-posts/unsubscribe";

    private static int _rateLimitKeyChecked;

    /// <summary>
    /// What the sign-up form's rate limit counts by: the visitor's IP address. Behind Container Apps' ingress, that's the
    /// rightmost X-Forwarded-For address (the AppHost sets <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>), which the ingress
    /// adds itself, so a visitor can't choose their own. If it were ever the ingress's private address, every visitor would
    /// share one limit (#137), so each replica logs which it is the first time, in production, without the address.
    /// </summary>
    public static string RateLimitKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (Interlocked.Exchange(ref _rateLimitKeyChecked, 1) == 0 && context.RequestServices.GetRequiredService<IHostEnvironment>().IsProduction())
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AnnouncementEndpoints));
            if (address is not null && Content.PublicAddresses.IsPublic(address))
            {
                logger.LogInformation("The announcements sign-up limit counts by each visitor's public IP address.");
            }
            else
            {
                logger.LogWarning("The announcements sign-up limit got a private or missing IP address, so every visitor may share one limit. Check the forwarded headers.");
            }
        }

        return address?.ToString() ?? "unknown";
    }

    public static WebApplication MapAnnouncements(this WebApplication app)
    {
        // The sign-up form, on the announcements page and the home page between seasons. Binding the form checks antiforgery,
        // and the rate limit stops one visitor using it to send confirmation emails to a lot of addresses.
        app.MapPost("/announcements/subscribe", async ([FromForm] SubscribeForm form, AnnouncementSubscriptions subscriptions, CancellationToken cancellationToken) =>
            {
                var back = form.ReturnUrl == "home" ? "/" : Page;
                var anchor = form.ReturnUrl == "home" ? "#announcements" : "";

                // A bot filled in the trap: thank it and do nothing.
                if (!string.IsNullOrEmpty(form.Website))
                {
                    return Results.LocalRedirect($"{back}?done=check-inbox{anchor}");
                }

                if (!Validator.TryValidateObject(form, new ValidationContext(form), null, validateAllProperties: true))
                {
                    return Results.LocalRedirect($"{back}?error=email{anchor}");
                }

                await subscriptions.SubscribeAsync(form.Email!, form.DailyPosts, cancellationToken);
                return Results.LocalRedirect($"{back}?done=check-inbox{anchor}");
            })
            .RequireRateLimiting(RateLimitPolicy);

        // The links in emails. A GET (a click, or a mail scanner checking the link) only shows a page with a button; a POST
        // acts. Mail providers' one-click unsubscribe (RFC 8058) POSTs to the stop link without an antiforgery token, so the
        // protected token in the link is what authorizes both.
        app.MapGet(AnnouncementSubscriptions.ConfirmPath, (string? token) => Results.Redirect($"{ConfirmPage}?token={Uri.EscapeDataString(token ?? "")}"));
        app.MapPost(AnnouncementSubscriptions.ConfirmPath, async (string? token, AnnouncementSubscriptions subscriptions, CancellationToken cancellationToken) =>
            {
                var done = subscriptions.TryReadConfirmToken(token, out var subscriberId, out var dailyPosts)
                    && await subscriptions.ConfirmAsync(subscriberId, dailyPosts, cancellationToken);
                return Results.Redirect($"{ConfirmPage}?token={Uri.EscapeDataString(token ?? "")}&done={(done ? "true" : "false")}");
            })
            .DisableAntiforgery();

        app.MapGet(AnnouncementSubscriptions.StopPath, (string? token) => Results.Redirect($"{UnsubscribePage}?token={Uri.EscapeDataString(token ?? "")}"));
        app.MapPost(AnnouncementSubscriptions.StopPath, async (string? token, AnnouncementSubscriptions subscriptions, CancellationToken cancellationToken) =>
            {
                // A valid link unsubscribes, even if they already had: either way they're off the list.
                if (!subscriptions.TryReadStopToken(token, out var subscriberId))
                {
                    return Results.Redirect($"{UnsubscribePage}?token={Uri.EscapeDataString(token ?? "")}");
                }

                await subscriptions.UnsubscribeAsync(subscriberId, cancellationToken);
                return Results.Redirect($"{UnsubscribePage}?done=true");
            })
            .DisableAntiforgery();

        // The link in the daily posts emails (#206) turns off only those, and keeps the address on the list. The token stays on
        // the page afterwards, so it can offer to unsubscribe from everything too.
        app.MapGet(AnnouncementSubscriptions.DailyPostsStopPath, (string? token) =>
            Results.Redirect($"{DailyPostsUnsubscribePage}?token={Uri.EscapeDataString(token ?? "")}"));
        app.MapPost(AnnouncementSubscriptions.DailyPostsStopPath, async (string? token, AnnouncementSubscriptions subscriptions, CancellationToken cancellationToken) =>
            {
                var done = subscriptions.TryReadDailyPostsStopToken(token, out var subscriberId) && await subscriptions.StopDailyPostsAsync(subscriberId, cancellationToken);
                return Results.Redirect($"{DailyPostsUnsubscribePage}?token={Uri.EscapeDataString(token ?? "")}&done={(done ? "true" : "false")}");
            })
            .DisableAntiforgery();

        if (app.Environment.IsDevelopment())
        {
            // Development only: queue and deliver announcements as if it were the given time. Everything goes to MailPit.
            app.MapPost("/dev/announcements/run", async (DateTimeOffset at, int? year, AnnouncementService announcements, CancellationToken cancellationToken) =>
                Results.Ok(new { sent = await announcements.RunAsync(at.ToUniversalTime(), year, cancellationToken) }));
        }

        return app;
    }
}
