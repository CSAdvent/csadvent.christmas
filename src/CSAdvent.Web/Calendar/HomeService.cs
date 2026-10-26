using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Calendar;

/// <summary>
/// What the home page shows: a season's calendar from when its sign-ups open until the end of December, and a page between
/// seasons the rest of the year. Both come from the season records in the database, so switching between them never needs
/// a code change or a redeploy.
/// </summary>
public sealed class HomeService(AdventDbContext db, AdventSchedule schedule, SeasonAccess access)
{
    public async Task<HomePage> GetAsync(CancellationToken cancellationToken = default)
    {
        // The test season only exists here for a tester in test mode.
        var inTestMode = access.InTestMode;
        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year != Season.TestYear || inTestMode)
            .OrderBy(s => s.Year)
            .Select(s => new SeasonDates(s.Year, s.ClaimsOpenAt, s.Year == Season.TestYear))
            .ToListAsync(cancellationToken);
        var plan = Plan(seasons, schedule);

        if (plan.CalendarYear is { } year)
        {
            // One query for the season and all of its slots, with their contributors for the GitHub links.
            var season = await db.Seasons
                .AsNoTracking()
                .Include(s => s.Slots).ThenInclude(slot => slot.Contributor)
                .SingleAsync(s => s.Year == year, cancellationToken);

            return new HomePage(SeasonCalendar.From(season, schedule), LastSeason: null, NextSeason: null);
        }

        FinishedSeason? lastSeason = null;
        if (plan.LastYear is { } lastYear)
        {
            var authors = await db.Slots
                .Where(s => s.SeasonYear == lastYear && s.PostUrl != null && s.AuthorName != null)
                .Select(s => s.AuthorName!)
                .ToListAsync(cancellationToken);
            var hasRetrospective = await db.Retrospectives.AnyAsync(r => r.SeasonYear == lastYear && r.PublishedAt != null, cancellationToken);
            lastSeason = new FinishedSeason(lastYear, authors.Count, authors.Distinct(StringComparer.OrdinalIgnoreCase).Count(), hasRetrospective);
        }

        return new HomePage(Calendar: null, lastSeason, plan.NextSeason);
    }

    /// <summary>The countdown to registration starts on this date in the season's year.</summary>
    public static (int Month, int Day) CountdownFrom { get; } = (9, 1);

    /// <summary>
    /// The calendar shown is the earliest season within its calendar dates (<see cref="AdventSchedule.IsCalendarShown"/>), so
    /// a season created early for next year never replaces this year's. Between seasons, the page leads with the next season
    /// (#200): when its sign-ups open, if it's been created and scheduled, counting the days from <see cref="CountdownFrom"/>.
    /// Below that, it thanks everyone for the latest finished season.
    /// <para>
    /// In test mode the test season is among the seasons, and it's the calendar shown, whatever the date.
    /// </para>
    /// </summary>
    public static HomePlan Plan(IReadOnlyList<SeasonDates> seasonsInOrder, AdventSchedule schedule)
    {
        var shown = seasonsInOrder.FirstOrDefault(s => s.IsTest)
            ?? seasonsInOrder.FirstOrDefault(s => schedule.IsCalendarShown(s.Year, s.ClaimsOpenAt));
        if (shown is not null)
        {
            return new HomePlan(shown.Year, LastYear: null, NextSeason: null);
        }

        var last = seasonsInOrder.LastOrDefault(s => schedule.IsFinished(s.Year));
        var next = seasonsInOrder.FirstOrDefault(s => !schedule.IsFinished(s.Year));
        return new HomePlan(CalendarYear: null, last?.Year, next is null ? null : Upcoming(next, schedule));
    }

    private static UpcomingSeason Upcoming(SeasonDates season, AdventSchedule schedule)
    {
        if (season.ClaimsOpenAt is not { } opensAt)
        {
            return new UpcomingSeason(season.Year, SignUpsOpenOn: null, DaysUntilSignUps: null);
        }

        var opensOn = DateOnly.FromDateTime(schedule.ToLocalTime(opensAt).DateTime);
        var today = schedule.Today;
        var counting = today >= new DateOnly(season.Year, CountdownFrom.Month, CountdownFrom.Day) && today <= opensOn;
        return new UpcomingSeason(season.Year, opensOn, counting ? opensOn.DayNumber - today.DayNumber : null);
    }
}

public sealed record SeasonDates(int Year, DateTimeOffset? ClaimsOpenAt, bool IsTest = false);

/// <summary>Which season's calendar to show, or between seasons, the last finished season and the next one.</summary>
public sealed record HomePlan(int? CalendarYear, int? LastYear, UpcomingSeason? NextSeason);

/// <summary>What the home page shows: a season's calendar, or between seasons, the last season's thanks and the next one.</summary>
public sealed record HomePage(SeasonCalendar? Calendar, FinishedSeason? LastSeason, UpcomingSeason? NextSeason)
{
    public bool IsBetweenSeasons => Calendar is null;
}

/// <summary>The latest finished season, for the thanks between seasons.</summary>
/// <param name="HasRetrospective">Whether its retrospective is published, so the thanks can link to it (#200).</param>
public sealed record FinishedSeason(int Year, int PostCount, int AuthorCount, bool HasRetrospective = false);

/// <summary>The next season, when its registration opens if that's scheduled, and the days left once the countdown has started.</summary>
public sealed record UpcomingSeason(int Year, DateOnly? SignUpsOpenOn, int? DaysUntilSignUps);
