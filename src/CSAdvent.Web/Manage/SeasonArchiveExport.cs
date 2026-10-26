using CSAdvent.Web.Auth;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

/// <summary>
/// A finished season as an archive file (#182), written like the ones in <c>Data/Archive</c>, so the organizers can commit it
/// there each January: then any new database is seeded with it (<see cref="ArchiveSeeder"/>), and the season's history no
/// longer depends on the database's 14-day backups. It holds only what the calendar shows: no contact details.
/// </summary>
public sealed class SeasonArchiveExport(AdventDbContext db, AdventSchedule schedule)
{
    /// <summary>The season's file, or null if there's no such season, it isn't finished yet, or it's the test season.</summary>
    public async Task<ArchivedSeason?> GetAsync(int year, CancellationToken cancellationToken = default)
    {
        // Finished means its last day is over, so every post in it has been revealed.
        if (year == Season.TestYear || !schedule.IsDayOver(year, Season.DaysInAdvent))
        {
            return null;
        }

        var season = await db.Seasons.AsNoTracking().Include(s => s.Slots).SingleOrDefaultAsync(s => s.Year == year, cancellationToken);
        return season is null ? null : ArchivedSeason.From(season);
    }
}

public static class SeasonArchiveEndpoints
{
    /// <summary>The download behind "Download {year}.json" on the seasons page. Save it as <c>Data/Archive/{year}.json</c>.</summary>
    public static string Link(int year) => $"manage/seasons/{year}/archive.json";

    public static WebApplication MapSeasonArchive(this WebApplication app)
    {
        app.MapGet("/manage/seasons/{year:int}/archive.json", async (int year, SeasonArchiveExport export, CancellationToken cancellationToken) =>
                await export.GetAsync(year, cancellationToken) is { } season
                    ? Results.File(ArchiveSeeder.Serialize(season), "application/json", $"{year}.json")
                    : Results.NotFound())
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }
}
