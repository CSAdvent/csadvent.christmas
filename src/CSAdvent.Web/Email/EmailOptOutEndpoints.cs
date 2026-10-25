using CSAdvent.Web.Auth;
using CSAdvent.Web.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Email;

public static class EmailOptOutEndpoints
{
    public static WebApplication MapEmailOptOuts(this WebApplication app)
    {
        // The link in every email. A GET (a click, or a mail scanner checking the link) only shows a confirmation; a POST
        // opts out. Mail providers' one-click unsubscribe (RFC 8058) POSTs here too, without an antiforgery token, so the
        // protected token in the link is what authorizes it.
        app.MapGet(EmailOptOutLinks.StopPath, (string? token) => Results.Redirect(Page(token, null)));
        app.MapPost(EmailOptOutLinks.StopPath, async (string? token, EmailOptOutLinks links, EmailOptOuts optOuts, CancellationToken cancellationToken) =>
            {
                if (!links.TryReadToken(token, out var address))
                {
                    return Results.Redirect(Page(token, null));
                }

                await optOuts.OptOutAsync(address, cancellationToken);
                return Results.Redirect(Page(token, "stopped"));
            })
            .DisableAntiforgery();

        // "Changed your mind?" on the confirmation page. The token says whose address it is, as above.
        app.MapPost(EmailOptOutLinks.ResumePath, async (string? token, EmailOptOutLinks links, EmailOptOuts optOuts, CancellationToken cancellationToken) =>
            {
                if (!links.TryReadToken(token, out var address))
                {
                    return Results.Redirect(Page(token, null));
                }

                await optOuts.OptInAsync(address, cancellationToken);
                return Results.Redirect(Page(token, "resumed"));
            })
            .DisableAntiforgery();

        // Signed in: turn emails back on for the verified email on their account, from their profile.
        app.MapPost("/profile/emails/resume", async (HttpContext context, IAntiforgery antiforgery, AdventDbContext db, EmailOptOuts optOuts, CancellationToken cancellationToken) =>
            {
                if (!await antiforgery.IsRequestValidAsync(context))
                {
                    return Results.BadRequest();
                }

                var contributorId = context.User.GetContributorId();
                var email = await db.Contributors
                    .Where(c => c.Id == contributorId && c.EmailVerifiedAt != null)
                    .Select(c => c.Email)
                    .SingleOrDefaultAsync(cancellationToken);
                if (email is not null)
                {
                    await optOuts.OptInAsync(email, cancellationToken);
                }

                return Results.LocalRedirect("/profile?done=emails-on");
            })
            .RequireAuthorization();

        return app;
    }

    private static string Page(string? token, string? done) =>
        $"{EmailOptOutLinks.PagePath}?token={Uri.EscapeDataString(token ?? "")}{(done is null ? "" : $"&done={done}")}";
}
