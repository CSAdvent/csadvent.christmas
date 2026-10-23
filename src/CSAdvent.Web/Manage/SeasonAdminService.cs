using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

/// <summary>A season for the organizer pages. <paramref name="IsFinished"/> once its last day is over, and it's history.</summary>
public sealed record SeasonSummary(int Year, int SlotsPerDay, DateTimeOffset? ClaimsOpenAt, int Spots, int ClaimedSpots, int PendingRequests, int Posts, bool IsFinished);

public enum CreateSeasonOutcome
{
    Created,
    AlreadyExists,
}

/// <summary>Creating seasons and scheduling their sign-ups. Creating a season only adds rows; earlier seasons are never touched.</summary>
public sealed class SeasonAdminService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider)
{
    public async Task<List<SeasonSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        // The test season has its own page (/manage/testing), and is never the current season or history.
        var seasons = await db.Seasons
            .Where(s => s.Year != Season.TestYear)
            .OrderByDescending(s => s.Year)
            .Select(s => new
            {
                s.Year,
                s.SlotsPerDay,
                s.ClaimsOpenAt,
                Spots = s.Slots.Count,
                ClaimedSpots = s.Slots.Count(slot => slot.AuthorName != null),
                PendingRequests = db.ClaimRequests.Count(r => r.Slot.SeasonYear == s.Year && r.Status == ClaimRequestStatus.Pending),
                Posts = s.Slots.Count(slot => slot.PostUrl != null && slot.AuthorName != null),
            })
            .ToListAsync(cancellationToken);

        return [.. seasons.Select(s => new SeasonSummary(s.Year, s.SlotsPerDay, s.ClaimsOpenAt, s.Spots, s.ClaimedSpots, s.PendingRequests, s.Posts, IsFinished(s.Year)))];
    }

    /// <summary>A season is history once its last day is over: the end of December 25 (or of its rehearsal's last day).</summary>
    public bool IsFinished(int year) => schedule.IsDayOver(year, Season.DaysInAdvent);

    /// <summary>
    /// The season the organizer pages work on: the earliest one that isn't finished, so a season created early for next year
    /// never replaces this year's. None once the last one is finished; past seasons are opened from the overview.
    /// </summary>
    public async Task<int?> GetCurrentYearAsync(CancellationToken cancellationToken = default)
    {
        var years = await db.Seasons.Where(s => s.Year != Season.TestYear).OrderBy(s => s.Year).Select(s => s.Year).ToListAsync(cancellationToken);
        return CurrentYear(years, IsFinished);
    }

    public static int? CurrentYear(IEnumerable<int> yearsInOrder, Func<int, bool> isFinished) =>
        yearsInOrder.Cast<int?>().FirstOrDefault(year => !isFinished(year!.Value));

    /// <summary>The first year from this one that doesn't have a season yet.</summary>
    public async Task<int> GetNextYearAsync(CancellationToken cancellationToken = default)
    {
        var years = await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken);
        var year = schedule.Today.Year;
        while (years.Contains(year))
        {
            year++;
        }

        return year;
    }

    public async Task<CreateSeasonOutcome> CreateAsync(int year, int slotsPerDay, DateTimeOffset? claimsOpenAt, CancellationToken cancellationToken = default)
    {
        if (await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
        {
            return CreateSeasonOutcome.AlreadyExists;
        }

        var season = Season.Create(year, slotsPerDay, timeProvider.GetUtcNow());
        season.ClaimsOpenAt = claimsOpenAt;
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        return CreateSeasonOutcome.Created;
    }

    public async Task<bool> SetClaimsOpenAtAsync(int year, DateTimeOffset? claimsOpenAt, CancellationToken cancellationToken = default) =>
        await db.Seasons
            .Where(s => s.Year == year)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.ClaimsOpenAt, claimsOpenAt), cancellationToken) > 0;
}
