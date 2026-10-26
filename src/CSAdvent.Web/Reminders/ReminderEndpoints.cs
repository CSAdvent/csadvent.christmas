namespace CSAdvent.Web.Reminders;

public static class ReminderEndpoints
{
    public const string ConfirmPagePath = "/emails/daily/unsubscribe";

    public static WebApplication MapReminders(this WebApplication app)
    {
        // The link in every daily email. A GET (a click, or a mail scanner checking the link) only shows a confirmation;
        // a POST opts out. Mail providers' one-click unsubscribe (RFC 8058) POSTs here too, without an antiforgery token,
        // so the protected token in the link is what authorizes it.
        app.MapGet(DailyEmailOptOut.Path, (string? token) =>
            Results.Redirect($"{ConfirmPagePath}?token={Uri.EscapeDataString(token ?? "")}"));
        app.MapPost(DailyEmailOptOut.Path, async (string? token, DailyEmailOptOut optOut, CancellationToken cancellationToken) =>
            {
                var done = optOut.TryReadToken(token, out var slotId) && await optOut.OptOutAsync(slotId, cancellationToken);
                return Results.Redirect($"{ConfirmPagePath}?token={Uri.EscapeDataString(token ?? "")}&done={(done ? "true" : "false")}");
            })
            .DisableAntiforgery();

        if (app.Environment.IsDevelopment())
        {
            // Development only: runs the reminders as if it were the given time, so they can be tried (and tested) without
            // waiting for December. Everything goes to MailPit.
            // In UTC whatever offset it's given, because PostgreSQL's timestamps only take UTC.
            app.MapPost("/dev/reminders/run", async (DateTimeOffset at, ReminderService reminders, CancellationToken cancellationToken) =>
                Results.Ok(await reminders.RunAsync(at.ToUniversalTime(), cancellationToken)));

            // The organizers' morning digest (#171) as if it were the given time, for one season or all: how many went.
            app.MapPost("/dev/digest/run", async (DateTimeOffset at, int? year, Manage.MorningDigest digest, CancellationToken cancellationToken) =>
                Results.Ok(new { Sent = await digest.RunAsync(at.ToUniversalTime(), year, cancellationToken) }));
        }

        return app;
    }
}
