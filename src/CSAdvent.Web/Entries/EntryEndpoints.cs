using CSAdvent.Web.Data;

namespace CSAdvent.Web.Entries;

public static class EntryEndpoints
{
    public static WebApplication MapEntries(this WebApplication app)
    {
        // "Read the post": counts the click (people only, nothing personal) and sends the reader on to the author's post.
        // Only a revealed entry's own post URL is ever a destination, so this can't be used as an open redirect.
        app.MapGet("/go/{year:int}/{day:int}/{part:int}", async (int year, int day, int part, string? via, HttpRequest request, EntryService entries, CancellationToken cancellationToken) =>
        {
            if (await entries.FindPostAsync(year, day, part, cancellationToken) is not { } post)
            {
                return Results.NotFound();
            }

            if (EntryTraffic.IsPerson(request))
            {
                await entries.RecordAsync(post.SlotId, EntryHitKind.Click, via, cancellationToken);
            }

            return Results.Redirect(post.PostUrl);
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
}
