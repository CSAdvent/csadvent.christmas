using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

public sealed record SeasonSummary(int Year, int SlotsPerDay, DateTimeOffset? ClaimsOpenAt, int Spots, int ClaimedSpots, int PendingRequests);

public enum CreateSeasonOutcome
{
    Created,
    AlreadyExists,
}

/// <summary>Creating seasons and scheduling their sign-ups. Creating a season only adds rows; earlier seasons are never touched.</summary>
public sealed class SeasonAdminService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider)
{
    public Task<List<SeasonSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Seasons
            .OrderByDescending(s => s.Year)
            .Select(s => new SeasonSummary(
                s.Year,
                s.SlotsPerDay,
                s.ClaimsOpenAt,
                s.Slots.Count,
                s.Slots.Count(slot => slot.AuthorName != null),
                db.ClaimRequests.Count(r => r.Slot.SeasonYear == s.Year && r.Status == ClaimRequestStatus.Pending)))
            .ToListAsync(cancellationToken);

    /// <summary>The season organizers work on: the same one the home page shows.</summary>
    public async Task<int?> GetCurrentYearAsync(CancellationToken cancellationToken = default)
    {
        var years = await db.Seasons.OrderBy(s => s.Year).Select(s => s.Year).ToListAsync(cancellationToken);
        return HomeService.SelectSeason(years, schedule.IsSeasonOver).Year;
    }

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
