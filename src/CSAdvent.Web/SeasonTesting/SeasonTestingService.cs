using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>What's been done in the test season: everything resetting it deletes.</summary>
public sealed record TestActivity(int Spots, int Requests, int BackupPosters, int BackupOffers, int Emails, int Visits, int SocialPosts)
{
    public bool IsEmpty => Spots + Requests + BackupPosters + BackupOffers + Emails + Visits + SocialPosts == 0;
}

public sealed record TestingView(DateTimeOffset StartedAt, DateOnly RehearsalStartsOn, IReadOnlyList<string> Testers, TestActivity Activity);

/// <summary>
/// The hidden test season (<see cref="Season.TestYear"/>): invited testers try everything for real on the live site,
/// following a rehearsal schedule, and resetting it deletes everything they did. It has nothing to do with the real seasons,
/// so it can be used any time, including during Advent to try something new.
/// </summary>
public sealed class SeasonTestingService(
    AdventDbContext db,
    TestSeasons testSeasons,
    TimeProvider timeProvider,
    BlurbCheckService blurbs,
    ReminderService reminders,
    BackupService backups)
{
    private const int Year = Season.TestYear;

    /// <summary>The test season, or null until an organizer creates it.</summary>
    public async Task<TestingView?> GetAsync(CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year == Year && s.TestingStartedAt != null && s.RehearsalStartsOn != null)
            .Select(s => new { s.TestingStartedAt, s.RehearsalStartsOn, Testers = s.Testers.OrderBy(t => t.GithubLogin).Select(t => t.GithubLogin).ToList() })
            .SingleOrDefaultAsync(cancellationToken);

        return season is null
            ? null
            : new TestingView(season.TestingStartedAt!.Value, season.RehearsalStartsOn!.Value, season.Testers, await GetActivityAsync(cancellationToken));
    }

    /// <summary>Creates the test season, with every spot open. Returns false if it already exists.</summary>
    public async Task<bool> CreateAsync(DateOnly rehearsalStartsOn, CancellationToken cancellationToken = default)
    {
        if (await db.Seasons.AnyAsync(s => s.Year == Year, cancellationToken))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var season = Season.Create(Year, Season.MaxSlotsPerDay, now);
        season.TestingStartedAt = now;
        season.RehearsalStartsOn = rehearsalStartsOn;
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
        return true;
    }

    /// <summary>Moves the rehearsal, for example to jump ahead to a later day.</summary>
    public async Task MoveAsync(DateOnly rehearsalStartsOn, CancellationToken cancellationToken = default)
    {
        await db.Seasons.Where(s => s.Year == Year).ExecuteUpdateAsync(s => s.SetProperty(x => x.RehearsalStartsOn, rehearsalStartsOn), cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
    }

    /// <summary>Adds a tester by GitHub login (or @login, or their GitHub profile URL). Returns false if it isn't a GitHub login.</summary>
    public async Task<bool> AddTesterAsync(string login, CancellationToken cancellationToken = default)
    {
        if (GitHubLogins.Normalize(login) is not { } normalized || !await db.Seasons.AnyAsync(s => s.Year == Year, cancellationToken))
        {
            return false;
        }

        if (!await db.SeasonTesters.AnyAsync(t => t.SeasonYear == Year && t.GithubLogin == normalized, cancellationToken))
        {
            db.SeasonTesters.Add(new SeasonTester { SeasonYear = Year, GithubLogin = normalized, AddedAt = timeProvider.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        await testSeasons.RefreshAsync(cancellationToken);
        return true;
    }

    public async Task RemoveTesterAsync(string login, CancellationToken cancellationToken = default)
    {
        var normalized = login.Trim().ToLowerInvariant();
        await db.SeasonTesters.Where(t => t.SeasonYear == Year && t.GithubLogin == normalized).ExecuteDeleteAsync(cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Does what the worker would do now for the test season, and only the test season (#162), rather than waiting up to 15
    /// minutes: checks its posts for the blurb first, as the worker does, so the daily email and the backups see the answer,
    /// then sends its due reminders and backup asks.
    /// </summary>
    public async Task<(BlurbRun Blurbs, ReminderRun Reminders, BackupRun Backups)> RunNowAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var checkedPosts = await blurbs.RunAsync(now, Year, cancellationToken);
        return (checkedPosts, await reminders.RunAsync(now, Year, cancellationToken), await backups.RunAsync(now, Year, cancellationToken));
    }

    /// <summary>
    /// Deletes everything done in the test season: every spot is open again, and the requests, backup list, offers, sent
    /// emails, visits and social posts are gone. The season, its rehearsal and its testers stay. Returns what was deleted, or
    /// null if there's no test season.
    /// </summary>
    public async Task<TestActivity?> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (!await db.Seasons.AnyAsync(s => s.Year == Year, cancellationToken))
        {
            return null;
        }

        var deleted = await GetActivityAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        await InTransactionAsync(
            async token =>
            {
                await DeleteActivityAsync(token);
                await db.Slots
                    .Where(s => s.SeasonYear == Year)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(x => x.AuthorName, (string?)null)
                            .SetProperty(x => x.AuthorUrl, (string?)null)
                            .SetProperty(x => x.PostTitle, (string?)null)
                            .SetProperty(x => x.PostUrl, (string?)null)
                            .SetProperty(x => x.PostSummary, (string?)null)
                            .SetProperty(x => x.ContributorId, (int?)null)
                            .SetProperty(x => x.DroppedOutAt, (DateTimeOffset?)null),
                        token);
                await db.Seasons.Where(s => s.Year == Year).ExecuteUpdateAsync(s => s.SetProperty(x => x.TestingStartedAt, now), token);
            },
            cancellationToken);

        await testSeasons.RefreshAsync(cancellationToken);
        return deleted;
    }

    /// <summary>
    /// Ends testing: deletes the test season itself, with its spots, testers, rehearsal and everything done in it, as if it had
    /// never been created. Testers who are still in test mode just see the real site. Returns false if there's no test season.
    /// </summary>
    public async Task<bool> EndAsync(CancellationToken cancellationToken = default)
    {
        if (!await db.Seasons.AnyAsync(s => s.Year == Year, cancellationToken))
        {
            return false;
        }

        await InTransactionAsync(
            async token =>
            {
                await DeleteActivityAsync(token);
                await db.Slots.Where(s => s.SeasonYear == Year).ExecuteDeleteAsync(token);
                await db.SeasonTesters.Where(t => t.SeasonYear == Year).ExecuteDeleteAsync(token);
                await db.OtherAdvents.Where(a => a.SeasonYear == Year).ExecuteDeleteAsync(token);
                await db.Seasons.Where(s => s.Year == Year).ExecuteDeleteAsync(token);
            },
            cancellationToken);

        await testSeasons.RefreshAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes the requests, backup list, offers and rounds, sent emails, visits, social posts, blurb checks (and the ones
    /// authors asked for) and contacts of the test season's spots.
    /// </summary>
    private async Task DeleteActivityAsync(CancellationToken token)
    {
        // Offers point at both the backup list and the spots, so they go first.
        await db.BackupOffers.Where(o => o.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.BackupRounds.Where(r => r.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.Substitutes.Where(s => s.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.BackupSignupEmails.Where(e => e.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.SentReminders.Where(r => r.Slot != null && r.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.EntryHits.Where(h => h.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.SocialPosts.Where(p => p.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.BlurbSelfChecks.Where(c => c.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.BlurbChecks.Where(c => c.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.ClaimRequests.Where(r => r.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
        await db.SlotContacts.Where(c => c.Slot.SeasonYear == Year).ExecuteDeleteAsync(token);
    }

    /// <summary>
    /// Runs the steps in one transaction. Aspire's database connection retries, which needs the whole transaction inside the
    /// retry: every step can run again.
    /// </summary>
    private async Task InTransactionAsync(Func<CancellationToken, Task> steps, CancellationToken cancellationToken) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                await steps(token);
                await transaction.CommitAsync(token);
            },
            cancellationToken);

    private async Task<TestActivity> GetActivityAsync(CancellationToken cancellationToken) => new(
        await db.Slots.CountAsync(s => s.SeasonYear == Year && (s.AuthorName != null || s.ContributorId != null || s.DroppedOutAt != null || s.PostUrl != null), cancellationToken),
        await db.ClaimRequests.CountAsync(r => r.Slot.SeasonYear == Year, cancellationToken),
        await db.Substitutes.CountAsync(s => s.SeasonYear == Year, cancellationToken),
        await db.BackupOffers.CountAsync(o => o.Slot.SeasonYear == Year, cancellationToken),
        await db.SentReminders.CountAsync(r => r.Slot != null && r.Slot.SeasonYear == Year, cancellationToken),
        await db.EntryHits.CountAsync(h => h.Slot.SeasonYear == Year, cancellationToken),
        await db.SocialPosts.CountAsync(p => p.Slot.SeasonYear == Year, cancellationToken));
}

/// <summary>What day today should be in the rehearsal (<see cref="RehearsalDays"/>): 0 is December 1.</summary>
public sealed class RehearsalForm
{
    [Required(ErrorMessage = "Choose what day today should be in the rehearsal.")]
    [Range(RehearsalDays.Earliest, RehearsalDays.Latest, ErrorMessage = "Choose what day today should be in the rehearsal.")]
    public int? TodayIs { get; set; }
}

public sealed class TesterForm
{
    [Required(ErrorMessage = "Enter a GitHub login.")]
    [StringLength(100)]
    public string Login { get; set; } = "";
}

public sealed class ResetTestingForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm that everything done in the test season should be deleted.")]
    public bool Confirm { get; set; }
}

public sealed class EndTestingForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm that the test season should be deleted.")]
    public bool Confirm { get; set; }
}
