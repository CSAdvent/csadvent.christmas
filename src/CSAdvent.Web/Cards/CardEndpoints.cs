using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Cards;

public static class CardEndpoints
{
    // Cards change only when their text does, and that changes their URL (?v=), so they can be cached for a day.
    private const string CacheControl = "public, max-age=86400";

    public static WebApplication MapCards(this WebApplication app)
    {
        // A revealed entry's card. Before the reveal there's no card, so nothing about the post leaks through it (#5).
        app.MapGet("/cards/{year:int}/{day:int}/{part:int}.jpg", async (int year, int day, int part, EntryService entries, CardRenderer cards, HttpResponse response, CancellationToken cancellationToken) =>
        {
            if (await entries.GetAsync(year, day, part, cancellationToken) is not { IsRevealed: true } entry)
            {
                return Results.NotFound();
            }

            response.Headers.CacheControl = CacheControl;
            return Results.File(cards.RenderEntry(CardFor(entry)), "image/jpeg");
        });

        // A season's card, with its post count once it's over.
        app.MapGet("/cards/{year:int}.jpg", async (int year, AdventDbContext db, AdventSchedule schedule, SeasonAccess access, CardRenderer cards, HttpResponse response, CancellationToken cancellationToken) =>
        {
            if (!access.CanSee(year) || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
            {
                return Results.NotFound();
            }

            int? postCount = schedule.IsSeasonOver(year)
                ? await db.Slots.CountAsync(s => s.SeasonYear == year && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null, cancellationToken)
                : null;
            response.Headers.CacheControl = CacheControl;
            return Results.File(cards.RenderSeason(year, postCount), "image/jpeg");
        });

        return app;
    }

    public static EntryCard CardFor(EntryView entry) =>
        new(entry.Year, entry.Day, entry.Part, entry.SlotsPerDay, entry.PostTitle!, entry.AuthorName!);
}
