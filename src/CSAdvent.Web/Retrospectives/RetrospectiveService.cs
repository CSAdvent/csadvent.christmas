using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Retrospectives;

public static class RetrospectiveLinks
{
    /// <summary>The season's retrospective, relative to the base URL.</summary>
    public static string Page(int year) => string.Create(CultureInfo.InvariantCulture, $"archive/{year}/retrospective");
}

/// <summary>A finished season on the organizers' list, with its retrospective's state.</summary>
public sealed record RetrospectiveSeason(int Year, int Posts, DateTimeOffset? UpdatedAt, DateTimeOffset? PublishedAt)
{
    public bool HasDraft => UpdatedAt is not null;
}

public enum RetrospectiveOutcome
{
    Saved,
    ChangedByOthers,
    NotFound,

    /// <summary>A published retrospective's draft can't be regenerated: unpublish it first.</summary>
    AlreadyPublished,
}

/// <summary>
/// Each finished season's retrospective (#62): its numbers (<see cref="RetrospectiveCalculator"/>), a first draft written
/// from them (<see cref="RetrospectiveDraft"/>), the organizers' edits, and publishing it at <c>/archive/{year}/retrospective</c>.
/// Never for the test season, which has nothing to look back on.
/// </summary>
public sealed class RetrospectiveService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider)
{
    /// <summary>Every finished season, newest first, with its retrospective's state.</summary>
    public async Task<IReadOnlyList<RetrospectiveSeason>> ListAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year != Season.TestYear)
            .OrderByDescending(s => s.Year)
            .Select(s => new
            {
                s.Year,
                Posts = s.Slots.Count(slot => slot.PostUrl != null),
                Retrospective = db.Retrospectives.Where(r => r.SeasonYear == s.Year).Select(r => new { r.UpdatedAt, r.PublishedAt }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return [.. seasons
            .Where(s => schedule.IsSeasonOver(s.Year))
            .Select(s => new RetrospectiveSeason(s.Year, s.Posts, s.Retrospective?.UpdatedAt, s.Retrospective?.PublishedAt))];
    }

    /// <summary>Whether the season can have a retrospective: it exists, it's real, and it's over.</summary>
    public async Task<bool> IsFinishedSeasonAsync(int year, CancellationToken cancellationToken = default) =>
        year != Season.TestYear && schedule.IsSeasonOver(year) && await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken);

    /// <summary>The season's retrospective, published or not, for the organizers.</summary>
    public Task<Retrospective?> GetAsync(int year, CancellationToken cancellationToken = default) =>
        db.Retrospectives.AsNoTracking().SingleOrDefaultAsync(r => r.SeasonYear == year, cancellationToken);

    /// <summary>The season's retrospective, if it's published.</summary>
    public Task<Retrospective?> GetPublishedAsync(int year, CancellationToken cancellationToken = default) =>
        db.Retrospectives.AsNoTracking().SingleOrDefaultAsync(r => r.SeasonYear == year && r.PublishedAt != null, cancellationToken);

    public Task<bool> IsPublishedAsync(int year, CancellationToken cancellationToken = default) =>
        db.Retrospectives.AnyAsync(r => r.SeasonYear == year && r.PublishedAt != null, cancellationToken);

    /// <summary>Every published retrospective, or one season's, newest season first.</summary>
    public async Task<IReadOnlyList<Retrospective>> GetAllPublishedAsync(int? year = null, CancellationToken cancellationToken = default) =>
        await db.Retrospectives
            .AsNoTracking()
            .Where(r => r.PublishedAt != null && (year == null || r.SeasonYear == year))
            .OrderByDescending(r => r.SeasonYear)
            .ToListAsync(cancellationToken);

    /// <summary>The season's numbers as they are now, or null if it can't have a retrospective.</summary>
    public async Task<RetrospectiveStats?> CalculateAsync(int year, CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year == year && s.Year != Season.TestYear)
            .Select(s => new { s.SlotsPerDay, s.ClaimsOpenAt, Spots = s.Slots.Count })
            .SingleOrDefaultAsync(cancellationToken);
        if (season is null || !schedule.IsSeasonOver(year))
        {
            return null;
        }

        // Every earlier season is over too, so they're all in the archive.
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear <= year && s.SeasonYear != Season.TestYear && s.PostUrl != null && s.AuthorName != null)
            .Select(s => new PublishedPost(s.Id, s.SeasonYear, s.Day, s.Part, s.PostTitle ?? "Untitled", s.AuthorName!, s.ContributorId))
            .ToListAsync(cancellationToken);

        // Npgsql only takes UTC, and the schedule's times are in the reveal time zone.
        var from = schedule.GetRevealTime(year, 1).ToUniversalTime();
        var to = schedule.GetCalendarEnd(year).ToUniversalTime();
        var hits = await db.EntryHits
            .AsNoTracking()
            .Where(h => h.Slot.SeasonYear == year && h.At >= from && h.At < to)
            .Select(h => new RecordedHit(h.SlotId, h.Kind, h.Via, h.At))
            .ToListAsync(cancellationToken);

        // Requests and backup posters are contact details, deleted on February 1 after the season (ContactRetention).
        var requests = await db.ClaimRequests.CountAsync(r => r.Slot.SeasonYear == year, cancellationToken);
        var firstDayEnds = season.ClaimsOpenAt?.AddDays(1);
        var requestsFirstDay = firstDayEnds is { } ends
            ? await db.ClaimRequests.CountAsync(r => r.Slot.SeasonYear == year && r.SubmittedAt < ends, cancellationToken)
            : 0;
        var backups = await db.Substitutes.CountAsync(s => s.SeasonYear == year && s.AssignedSlotId != null, cancellationToken);

        var social = await db.SocialPosts
            .Where(p => p.Slot.SeasonYear == year && p.Status == SocialPostStatus.Posted)
            .GroupBy(p => p.Platform)
            .Select(g => new { Platform = g.Key, Posts = g.Count() })
            .ToListAsync(cancellationToken);

        return RetrospectiveCalculator.Calculate(new RetrospectiveInput(
            year,
            season.SlotsPerDay,
            season.Spots,
            posts,
            hits,
            schedule.GetDate(year, 1),
            moment => DateOnly.FromDateTime(schedule.ToLocalTime(moment).DateTime),
            requests,
            requestsFirstDay,
            backups,
            [.. social.OrderBy(p => p.Platform).Select(p => new PlatformPosts(p.Platform, p.Posts))]));
    }

    /// <summary>
    /// Writes a new draft from the season's numbers as they are now, replacing the draft's text and numbers. A published
    /// retrospective is left alone.
    /// </summary>
    public async Task<RetrospectiveOutcome> GenerateAsync(int year, string generatedBy, CancellationToken cancellationToken = default)
    {
        if (await CalculateAsync(year, cancellationToken) is not { } stats)
        {
            return RetrospectiveOutcome.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        var retrospective = await db.Retrospectives.SingleOrDefaultAsync(r => r.SeasonYear == year, cancellationToken);
        if (retrospective is { IsPublished: true })
        {
            return RetrospectiveOutcome.AlreadyPublished;
        }

        if (retrospective is null)
        {
            db.Retrospectives.Add(new Retrospective
            {
                SeasonYear = year,
                Title = RetrospectiveDraft.Title(year),
                Body = RetrospectiveDraft.Body(stats),
                Stats = stats,
                StatsAt = now,
                UpdatedAt = now,
                UpdatedBy = generatedBy,
            });
        }
        else
        {
            retrospective.Title = RetrospectiveDraft.Title(year);
            retrospective.Body = RetrospectiveDraft.Body(stats);
            retrospective.Stats = stats;
            retrospective.StatsAt = now;
            retrospective.UpdatedAt = now;
            retrospective.UpdatedBy = generatedBy;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RetrospectiveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return RetrospectiveOutcome.ChangedByOthers;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another organizer generated it at the same moment.
            return RetrospectiveOutcome.ChangedByOthers;
        }
    }

    /// <summary>Saves the organizers' edits, unless someone else saved since they opened it (the form's version).</summary>
    public async Task<RetrospectiveOutcome> SaveAsync(int year, RetrospectiveForm form, string savedBy, CancellationToken cancellationToken = default)
    {
        var retrospective = await db.Retrospectives.SingleOrDefaultAsync(r => r.SeasonYear == year, cancellationToken);
        if (retrospective is null)
        {
            return RetrospectiveOutcome.NotFound;
        }

        db.Entry(retrospective).Property(r => r.Version).OriginalValue = form.Version;
        retrospective.Title = form.Title.Trim();
        retrospective.Body = form.Body.Trim().ReplaceLineEndings("\n");
        retrospective.ShowCharts = form.ShowCharts;
        retrospective.UpdatedAt = timeProvider.GetUtcNow();
        retrospective.UpdatedBy = savedBy;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RetrospectiveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return RetrospectiveOutcome.ChangedByOthers;
        }
    }

    /// <summary>Generates a draft for every finished season that doesn't have one yet (the backfill). Returns how many.</summary>
    public async Task<int> GenerateMissingAsync(string generatedBy, CancellationToken cancellationToken = default)
    {
        var generated = 0;
        foreach (var season in (await ListAsync(cancellationToken)).Where(s => !s.HasDraft))
        {
            if (await GenerateAsync(season.Year, generatedBy, cancellationToken) is RetrospectiveOutcome.Saved)
            {
                generated++;
            }
        }

        return generated;
    }

    /// <summary>Publishes every draft at once. Returns how many.</summary>
    public async Task<int> PublishDraftsAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Retrospectives
            .Where(r => r.PublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PublishedAt, now), cancellationToken);
    }

    public async Task<bool> PublishAsync(int year, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Retrospectives
            .Where(r => r.SeasonYear == year && r.PublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PublishedAt, now), cancellationToken) == 1;
    }

    public async Task<bool> UnpublishAsync(int year, CancellationToken cancellationToken = default) =>
        await db.Retrospectives
            .Where(r => r.SeasonYear == year && r.PublishedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PublishedAt, (DateTimeOffset?)null), cancellationToken) == 1;
}

/// <summary>The organizers' edit of a retrospective.</summary>
public sealed class RetrospectiveForm
{
    [Required(ErrorMessage = "Give the retrospective a title.")]
    [StringLength(150, ErrorMessage = "Keep the title to 150 characters or fewer.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Write the retrospective.")]
    [StringLength(20000, ErrorMessage = "Keep the retrospective to 20,000 characters or fewer.")]
    public string Body { get; set; } = "";

    public bool ShowCharts { get; set; } = true;

    /// <summary>The retrospective's row version when the form was shown.</summary>
    public uint Version { get; set; }

    public static RetrospectiveForm From(Retrospective retrospective) => new()
    {
        Title = retrospective.Title,
        Body = retrospective.Body,
        ShowCharts = retrospective.ShowCharts,
        Version = retrospective.Version,
    };
}
