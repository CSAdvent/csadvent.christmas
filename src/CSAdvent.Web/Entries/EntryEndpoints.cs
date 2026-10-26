using System.Globalization;
using CSAdvent.Web.Components.Pages.Entries;
using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.AspNetCore.Components.Endpoints;

namespace CSAdvent.Web.Entries;

public static class EntryEndpoints
{
    public static WebApplication MapEntries(this WebApplication app)
    {
        // "Read the post", the calendar's titles and the daily email: counts the click (people only, nothing personal) and
        // sends the reader on to the author's post.
        app.MapGet("/go/{year:int}/{day:int}/{part:int}", async (int year, int day, int part, string? via, HttpContext context, EntryService entries) =>
        {
            context.Response.Headers["X-Robots-Tag"] = "noindex";
            return await entries.FindPostAsync(year, day, part, context.RequestAborted) is { } post
                ? await ToThePostAsync(context, entries, post.SlotId, post.PostUrl, via)
                : Results.NotFound();
        });

        // "A random post" (#205): the page of any post readers can see, from any season but the test season. Never cached, so
        // every visit gets another.
        app.MapGet("/random", async (RevealedPosts revealed, HttpResponse response, CancellationToken cancellationToken) =>
        {
            response.Headers.CacheControl = "no-store";
            var posts = await revealed.GetAsync(cancellationToken);
            return Results.Redirect(posts.Count == 0 ? "/archive" : $"/{posts[Random.Shared.Next(posts.Count)].Page}");
        });

        return app;
    }

    /// <summary>
    /// Readers go straight to the post (decided 2026-10-26). An entry page's address is still the link we publish, because
    /// link previews read the page for our card, but anyone else following it to a revealed post gets the same counted
    /// redirect as <c>/go</c>, not a page in between: people, and crawlers too, so they never see what readers don't and follow
    /// through to the author's post. Link previews (<see cref="EntryTraffic.LinkPreviewAgents"/>) and organizers get the page.
    /// It runs ahead of the page, once sign-in has, and leaves everything else to it: the page before the reveal, a spot
    /// without a post (a 404), and the test season for anyone who can't see it (a 404 too).
    /// </summary>
    public static WebApplication UseEntryRedirects(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (await PostToGoStraightToAsync(context) is { } post)
            {
                var entries = context.RequestServices.GetRequiredService<EntryService>();
                await (await ToThePostAsync(context, entries, post.SlotId, post.PostUrl, context.Request.Query["via"])).ExecuteAsync(context);
                return;
            }

            await next();
        });
        return app;
    }

    /// <summary>The post a request for an entry's page goes straight to, or nothing if it gets the page.</summary>
    private static async Task<(int SlotId, string PostUrl)?> PostToGoStraightToAsync(HttpContext context)
    {
        var request = context.Request;
        if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type != typeof(EntryPage)
            || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || EntryTraffic.IsLinkPreview(request)
            || context.RequestServices.GetRequiredService<SeasonAccess>().IsOrganizer
            || SpotOf(request.RouteValues) is not { } spot)
        {
            return null;
        }

        // A post without a title has no page (a 404), so its link doesn't lead anywhere either.
        var post = await context.RequestServices.GetRequiredService<EntryService>().FindPostAsync(spot.Year, spot.Day, spot.Part, context.RequestAborted);
        return post is { HasTitle: true } ? (post.Value.SlotId, post.Value.PostUrl) : null;
    }

    /// <summary>
    /// Counts a person's click and sends them on to the entry's own post. Only a revealed entry's own post URL is ever a
    /// destination, so neither link can be used as an open redirect. Crawlers may follow it to the post (#167), but never list
    /// the redirect itself, and nobody keeps it, because the post's link can change.
    /// </summary>
    private static async Task<IResult> ToThePostAsync(HttpContext context, EntryService entries, int slotId, string postUrl, string? via)
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex";
        context.Response.Headers.CacheControl = "private, no-store";
        if (EntryTraffic.IsClick(context.Request))
        {
            await entries.RecordAsync(slotId, EntryHitKind.Click, via, context.RequestAborted);
        }

        return Results.Redirect(postUrl);
    }

    /// <summary>The year, day and part in an entry page's address.</summary>
    private static (int Year, int Day, int Part)? SpotOf(RouteValueDictionary values) =>
        Number(values, "Year") is { } year && Number(values, "Day") is { } day && Number(values, "Part") is { } part ? (year, day, part) : null;

    private static int? Number(RouteValueDictionary values, string key) =>
        int.TryParse(values[key]?.ToString(), CultureInfo.InvariantCulture, out var number) ? number : null;
}
