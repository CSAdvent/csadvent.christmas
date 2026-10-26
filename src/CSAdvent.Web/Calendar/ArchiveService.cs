using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Calendar;

/// <summary>
/// Past seasons, read from the same tables as the current one. A season joins the archive as soon as its last day is
/// revealed, so there's no archiving step.
/// </summary>
/// <summary>Finished seasons. The test season can finish its rehearsal, but it's only listed for testers in test mode.</summary>
public sealed class ArchiveService(AdventDbContext db, AdventSchedule schedule, SeasonAccess access)
{
    public async Task<IReadOnlyList<ArchivedSeasonSummary>> GetSeasonsAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await db.Seasons
            .OrderByDescending(s => s.Year)
            .Select(s => new ArchivedSeasonSummary(s.Year, s.Slots.Count(slot => slot.PostUrl != null)))
            .ToListAsync(cancellationToken);

        return [.. seasons.Where(s => schedule.IsLastDayRevealed(s.Year) && (s.Year != Season.TestYear || access.InTestMode))];
    }

    /// <summary>The season's calendar, or null if there's no such season or it isn't over yet.</summary>
    public async Task<SeasonCalendar?> GetSeasonAsync(int year, CancellationToken cancellationToken = default)
    {
        // One query for the season and all of its slots, with their contributors for the GitHub links.
        var season = await db.Seasons
            .AsNoTracking()
            .Include(s => s.Slots).ThenInclude(slot => slot.Contributor)
            .SingleOrDefaultAsync(s => s.Year == year, cancellationToken);

        return season is not null && schedule.IsLastDayRevealed(season.Year) && access.CanSee(season.Year) ? SeasonCalendar.From(season, schedule) : null;
    }
}

public sealed record ArchivedSeasonSummary(int Year, int Posts);
