using System.Globalization;
using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.DeadLinks;

/// <summary>What the dead-link buttons post. Plain forms, because the dead links page has one per post.</summary>
public sealed class LinkCheckForm
{
    public int SlotId { get; set; }

    /// <summary>A new link for the post, or its archived copy.</summary>
    public string? Url { get; set; }

    /// <summary>The spot's row version when the page was shown, for a new link.</summary>
    public uint Version { get; set; }

    /// <summary>The organizer page to go back to: the dead links page or the entry's page.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>What a dead-link button just did, from the <c>links</c> query string the endpoints come back with: <c>checked-42</c>.</summary>
public static class LinkCheckDone
{
    /// <summary>What was just done to this spot's post ("checked", "invalid"…), or null if it was another post, or nothing.</summary>
    public static string? For(string? query, int slotId)
    {
        var dash = query?.LastIndexOf('-') ?? -1;
        return dash > 0 && int.TryParse(query.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id == slotId
            ? query![..dash]
            : null;
    }
}

public static class LinkCheckEndpoints
{
    public static WebApplication MapLinkChecks(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        var group = app.MapGroup("/manage/links").RequireAuthorization(AdventPolicies.Admin);

        group.MapPost("/check", async ([FromForm] LinkCheckForm form, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "checked", await links.CheckNowAsync(form.SlotId, clock.GetUtcNow(), cancellationToken)));

        group.MapPost("/working", async ([FromForm] LinkCheckForm form, HttpContext context, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "working", await links.MarkWorkingAsync(form.SlotId, Organizer(context), clock.GetUtcNow(), cancellationToken)));

        group.MapPost("/dead", async ([FromForm] LinkCheckForm form, HttpContext context, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "dead", await links.MarkDeadAsync(form.SlotId, Organizer(context), clock.GetUtcNow(), cancellationToken)));

        group.MapPost("/archive", async ([FromForm] LinkCheckForm form, HttpContext context, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "archive", await links.SetArchivedCopyAsync(form.SlotId, form.Url, Organizer(context), clock.GetUtcNow(), cancellationToken)));

        group.MapPost("/lookup", async ([FromForm] LinkCheckForm form, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "lookup", await links.LookUpCopyAsync(form.SlotId, clock.GetUtcNow(), cancellationToken)));

        group.MapPost("/replace", async ([FromForm] LinkCheckForm form, HttpContext context, LinkCheckService links, TimeProvider clock, CancellationToken cancellationToken) =>
            Back(form, "replaced", await links.ReplaceAsync(form.SlotId, form.Version, form.Url, Organizer(context), clock.GetUtcNow(), cancellationToken)));

        if (app.Environment.IsDevelopment())
        {
            // Development only: runs the check as if it were the given time. With the sample pages, nothing reaches authors'
            // sites or the Internet Archive.
            app.MapPost("/dev/links/run", async (DateTimeOffset at, int? year, LinkCheckService links, CancellationToken cancellationToken) =>
                Results.Ok(await links.RunAsync(at.ToUniversalTime(), year, cancellationToken)));
        }

        return app;
    }

    private static string Organizer(HttpContext context) => context.User.Identity?.Name ?? "organizer";

    /// <summary>
    /// Back to the organizer page the button was on (only the dead links page or an entry's page), saying what happened, at
    /// the post's box.
    /// </summary>
    private static IResult Back(LinkCheckForm form, string done, LinkActionOutcome outcome)
    {
        if (outcome is LinkActionOutcome.NotFound)
        {
            return Results.NotFound();
        }

        var target = form.ReturnUrl is { } url
            && (url.StartsWith(LinkCheckService.DeadLinksPage, StringComparison.Ordinal) || url.StartsWith("manage/entries/", StringComparison.Ordinal))
            && !url.Contains("//", StringComparison.Ordinal)
            && !url.Contains('\\', StringComparison.Ordinal)
            ? $"/{url}"
            : $"/{LinkCheckService.DeadLinksPage}#post-{form.SlotId}";
        var result = outcome switch
        {
            LinkActionOutcome.Invalid => "invalid",
            LinkActionOutcome.ChangedByOthers => "changed",
            _ => done,
        };
        return Results.LocalRedirect(ReturnUrl.WithQuery(target, "links", $"{result}-{form.SlotId}"));
    }
}
