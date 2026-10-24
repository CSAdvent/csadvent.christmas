using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Announcements;

public static class AnnouncementEndpoints
{
    public const string RateLimitPolicy = "announcements-subscribe";
    public const string Page = "/announcements";
    public const string ConfirmPage = "/announcements/confirm";
    public const string UnsubscribePage = "/announcements/unsubscribe";

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

                await subscriptions.SubscribeAsync(form.Email!, cancellationToken);
                return Results.LocalRedirect($"{back}?done=check-inbox{anchor}");
            })
            .RequireRateLimiting(RateLimitPolicy);

        // The links in emails. A GET (a click, or a mail scanner checking the link) only shows a page with a button; a POST
        // acts. Mail providers' one-click unsubscribe (RFC 8058) POSTs to the stop link without an antiforgery token, so the
        // protected token in the link is what authorizes both.
        app.MapGet(AnnouncementSubscriptions.ConfirmPath, (string? token) => Results.Redirect($"{ConfirmPage}?token={Uri.EscapeDataString(token ?? "")}"));
        app.MapPost(AnnouncementSubscriptions.ConfirmPath, async (string? token, AnnouncementSubscriptions subscriptions, CancellationToken cancellationToken) =>
            {
                var done = subscriptions.TryReadConfirmToken(token, out var subscriberId) && await subscriptions.ConfirmAsync(subscriberId, cancellationToken);
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

        if (app.Environment.IsDevelopment())
        {
            // Development only: queue and deliver announcements as if it were the given time. Everything goes to MailPit.
            app.MapPost("/dev/announcements/run", async (DateTimeOffset at, int? year, AnnouncementService announcements, CancellationToken cancellationToken) =>
                Results.Ok(new { sent = await announcements.RunAsync(at.ToUniversalTime(), year, cancellationToken) }));
        }

        return app;
    }
}
