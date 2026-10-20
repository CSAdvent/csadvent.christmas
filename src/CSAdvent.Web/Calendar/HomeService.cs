using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Calendar;

/// <summary>
/// What the home page shows: the current season's calendar, or off-season, the latest finished season. Both come from
/// the season records in the database, so switching between them never needs a code change or a redeploy.
/// </summary>
public sealed class HomeService(AdventDbContext db, AdventSchedule schedule)
{
    public async Task<HomePage> GetAsync(CancellationToken cancellationToken = default)
    {
        var years = await db.Seasons.OrderBy(s => s.Year).Select(s => s.Year).ToListAsync(cancellationToken);
        var (year, isOffSeason) = SelectSeason(years, schedule.IsSeasonOver);

        if (year is null)
        {
            return new HomePage(Calendar: null, IsOffSeason: true);
        }

        // One query for the season and all of its slots.
        var season = await db.Seasons
            .AsNoTracking()
            .Include(s => s.Slots)
            .SingleAsync(s => s.Year == year, cancellationToken);

        return new HomePage(SeasonCalendar.From(season, schedule), isOffSeason);
    }

    /// <summary>
    /// The current season is the earliest one that isn't over, so a season created early for next year never replaces
    /// this year's. With none, it's off-season and the latest season is shown instead.
    /// </summary>
    public static (int? Year, bool IsOffSeason) SelectSeason(IReadOnlyList<int> yearsInOrder, Func<int, bool> isSeasonOver)
    {
        foreach (var year in yearsInOrder)
        {
            if (!isSeasonOver(year))
            {
                return (year, false);
            }
        }

        return (yearsInOrder.Count > 0 ? yearsInOrder[^1] : null, true);
    }
}

public sealed record HomePage(SeasonCalendar? Calendar, bool IsOffSeason);
