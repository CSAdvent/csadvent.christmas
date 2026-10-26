using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Cards;

public static class CardEndpoints
{
    // Cards change only when their text does, and that changes their link's version (?v=). Asked for with the version the site
    // links to now, a card never changes, so it's kept for good (#186). Without one, or with an old one, it's kept for a day.
    private const string Versioned = "public, max-age=31536000, immutable";
    private const string Unversioned = "public, max-age=86400";

    public static WebApplication MapCards(this WebApplication app)
    {
        // A revealed entry's card. Before the reveal there's no card, so nothing about the post leaks through it (#5).
        app.MapGet("/cards/{year:int}/{day:int}/{part:int}.jpg", async (int year, int day, int part, EntryService entries, CardRenderer cards, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        {
            if (await entries.GetAsync(year, day, part, cancellationToken) is not { IsRevealed: true } entry)
            {
                return Results.NotFound();
            }

            var card = CardFor(entry);
            response.Headers.CacheControl = CacheControlFor(request, CardLinks.Entry(card));
            return Results.File(cards.RenderEntry(card), "image/jpeg");
        });

        // A season's card: with its post count once it's over, or before its sign-ups open, when they do.
        app.MapGet("/cards/{year:int}.jpg", async (int year, AdventDbContext db, AdventSchedule schedule, SeasonAccess access, CardRenderer cards, HttpRequest request, HttpResponse response, CancellationToken cancellationToken) =>
        {
            if (!access.CanSee(year) || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
            {
                return Results.NotFound();
            }

            int? postCount = schedule.IsSeasonOver(year)
                ? await db.Slots.CountAsync(s => s.SeasonYear == year && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null, cancellationToken)
                : null;
            var claimsOpenAt = await db.Seasons.Where(s => s.Year == year).Select(s => s.ClaimsOpenAt).SingleAsync(cancellationToken);
            DateOnly? signUpsOpenOn = postCount is null && claimsOpenAt is { } opensAt && !schedule.AreClaimsOpen(opensAt)
                ? DateOnly.FromDateTime(schedule.ToLocalTime(opensAt).DateTime)
                : null;
            var link = signUpsOpenOn is { } opensOn ? CardLinks.Upcoming(year, opensOn) : CardLinks.Season(year, postCount);
            response.Headers.CacheControl = CacheControlFor(request, link);
            return Results.File(cards.RenderSeason(year, postCount, signUpsOpenOn), "image/jpeg");
        });

        return app;
    }

    /// <summary>Kept for good when it's asked for by its current, versioned link; otherwise for a day.</summary>
    internal static string CacheControlFor(HttpRequest request, string link) =>
        link.Contains("?v=", StringComparison.Ordinal) && $"{request.Path}{request.QueryString}" == $"/{link}" ? Versioned : Unversioned;

    public static EntryCard CardFor(EntryView entry) =>
        new(entry.Year, entry.Day, entry.Part, entry.SlotsPerDay, entry.PostTitle!, entry.AuthorName!);
}
