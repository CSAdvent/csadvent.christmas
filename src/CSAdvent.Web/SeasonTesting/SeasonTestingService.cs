using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Data;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.SeasonTesting;

public enum StartTestingOutcome
{
    Started,
    NoSuchSeason,
    AlreadyTesting,

    /// <summary>Real people could be signing up, so their spots must never be wiped as test data.</summary>
    SignUpsOpen,

    /// <summary>The season already has spots, requests or backups that finishing testing would delete.</summary>
    HasActivity,
}

/// <summary>What's been done in a season: everything finishing testing deletes.</summary>
public sealed record TestActivity(int Spots, int Requests, int BackupPosters, int BackupOffers, int Emails, int Visits, int SocialPosts)
{
    public bool IsEmpty => Spots + Requests + BackupPosters + BackupOffers + Emails + Visits + SocialPosts == 0;
}

public sealed record TestingView(
    int Year,
    DateTimeOffset? ClaimsOpenAt,
    DateTimeOffset? TestingStartedAt,
    DateOnly? RehearsalStartsOn,
    IReadOnlyList<string> Testers,
    TestActivity Activity)
{
    public bool IsTesting => TestingStartedAt is not null;
}

/// <summary>
/// Testing a season before its sign-ups open: invited testers try everything for real on the live site, following a
/// rehearsal schedule, and finishing deletes everything they did. Only a season nobody has signed up for can be tested, so
/// finishing can never delete a real contributor's spot.
/// </summary>
public sealed partial class SeasonTestingService(
    AdventDbContext db,
    TestSeasons testSeasons,
    TimeProvider timeProvider,
    ReminderService reminders,
    BackupService backups)
{
    public async Task<TestingView?> GetAsync(int year, CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year == year)
            .Select(s => new { s.Year, s.ClaimsOpenAt, s.TestingStartedAt, s.RehearsalStartsOn, Testers = s.Testers.OrderBy(t => t.GithubLogin).Select(t => t.GithubLogin).ToList() })
            .SingleOrDefaultAsync(cancellationToken);

        return season is null
            ? null
            : new TestingView(season.Year, season.ClaimsOpenAt, season.TestingStartedAt, season.RehearsalStartsOn, season.Testers, await GetActivityAsync(year, cancellationToken));
    }

    public async Task<StartTestingOutcome> StartAsync(int year, DateOnly rehearsalStartsOn, CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons.SingleOrDefaultAsync(s => s.Year == year, cancellationToken);
        if (season is null)
        {
            return StartTestingOutcome.NoSuchSeason;
        }

        if (season.TestingStartedAt is not null)
        {
            return StartTestingOutcome.AlreadyTesting;
        }

        var now = timeProvider.GetUtcNow();
        if (season.ClaimsOpenAt is { } opensAt && opensAt <= now)
        {
            return StartTestingOutcome.SignUpsOpen;
        }

        if (!(await GetActivityAsync(year, cancellationToken)).IsEmpty)
        {
            return StartTestingOutcome.HasActivity;
        }

        season.TestingStartedAt = now;
        season.RehearsalStartsOn = rehearsalStartsOn;
        await db.SaveChangesAsync(cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
        return StartTestingOutcome.Started;
    }

    /// <summary>Moves the rehearsal, for example to jump ahead to a later day. Returns false if the season isn't being tested.</summary>
    public async Task<bool> MoveAsync(int year, DateOnly rehearsalStartsOn, CancellationToken cancellationToken = default)
    {
        var moved = await db.Seasons
            .Where(s => s.Year == year && s.TestingStartedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RehearsalStartsOn, rehearsalStartsOn), cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
        return moved > 0;
    }

    /// <summary>Adds a tester by GitHub login (or @login, or their GitHub profile URL). Returns false if it isn't a GitHub login.</summary>
    public async Task<bool> AddTesterAsync(int year, string login, CancellationToken cancellationToken = default)
    {
        if (NormalizeLogin(login) is not { } normalized || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
        {
            return false;
        }

        if (!await db.SeasonTesters.AnyAsync(t => t.SeasonYear == year && t.GithubLogin == normalized, cancellationToken))
        {
            db.SeasonTesters.Add(new SeasonTester { SeasonYear = year, GithubLogin = normalized, AddedAt = timeProvider.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }

        await testSeasons.RefreshAsync(cancellationToken);
        return true;
    }

    public async Task RemoveTesterAsync(int year, string login, CancellationToken cancellationToken = default)
    {
        var normalized = login.Trim().ToLowerInvariant();
        await db.SeasonTesters.Where(t => t.SeasonYear == year && t.GithubLogin == normalized).ExecuteDeleteAsync(cancellationToken);
        await testSeasons.RefreshAsync(cancellationToken);
    }

    /// <summary>Sends whatever reminders and backup asks are due now, rather than waiting up to 15 minutes for the worker.</summary>
    public async Task<(ReminderRun Reminders, BackupRun Backups)> RunNowAsync(int year, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return (await reminders.RunAsync(now, cancellationToken), await backups.RunAsync(now, year, cancellationToken));
    }

    /// <summary>
    /// Ends testing and deletes everything done in the season: every spot is open again, and the requests, backup list, offers,
    /// sent emails, visits and social posts are gone. The season itself, its sign-up date, its testers and its Other Advents
    /// list stay. Returns what was deleted, or null if the season isn't being tested.
    /// </summary>
    public async Task<TestActivity?> FinishAsync(int year, CancellationToken cancellationToken = default)
    {
        if (!await db.Seasons.AnyAsync(s => s.Year == year && s.TestingStartedAt != null, cancellationToken))
        {
            return null;
        }

        var deleted = await GetActivityAsync(year, cancellationToken);

        // Aspire's database connection retries, which needs the whole transaction inside the retry: every step can run again.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async token =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                // Offers point at both the backup list and the spots, so they go first.
                await db.BackupOffers.Where(o => o.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.Substitutes.Where(s => s.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.SentReminders.Where(r => r.Slot != null && r.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.EntryHits.Where(h => h.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.SocialPosts.Where(p => p.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.ClaimRequests.Where(r => r.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.SlotContacts.Where(c => c.Slot.SeasonYear == year).ExecuteDeleteAsync(token);
                await db.Slots
                    .Where(s => s.SeasonYear == year)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(x => x.AuthorName, (string?)null)
                            .SetProperty(x => x.AuthorUrl, (string?)null)
                            .SetProperty(x => x.PostTitle, (string?)null)
                            .SetProperty(x => x.PostUrl, (string?)null)
                            .SetProperty(x => x.ContributorId, (int?)null)
                            .SetProperty(x => x.DroppedOutAt, (DateTimeOffset?)null),
                        token);
                await db.Seasons
                    .Where(s => s.Year == year)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.TestingStartedAt, (DateTimeOffset?)null).SetProperty(x => x.RehearsalStartsOn, (DateOnly?)null),
                        token);
                await transaction.CommitAsync(token);
            },
            cancellationToken);

        await testSeasons.RefreshAsync(cancellationToken);
        return deleted;
    }

    private async Task<TestActivity> GetActivityAsync(int year, CancellationToken cancellationToken) => new(
        await db.Slots.CountAsync(s => s.SeasonYear == year && (s.AuthorName != null || s.ContributorId != null || s.DroppedOutAt != null || s.PostUrl != null), cancellationToken),
        await db.ClaimRequests.CountAsync(r => r.Slot.SeasonYear == year, cancellationToken),
        await db.Substitutes.CountAsync(s => s.SeasonYear == year, cancellationToken),
        await db.BackupOffers.CountAsync(o => o.Slot.SeasonYear == year, cancellationToken),
        await db.SentReminders.CountAsync(r => r.Slot != null && r.Slot.SeasonYear == year, cancellationToken),
        await db.EntryHits.CountAsync(h => h.Slot.SeasonYear == year, cancellationToken),
        await db.SocialPosts.CountAsync(p => p.Slot.SeasonYear == year, cancellationToken));

    /// <summary>A GitHub login, lowercase, from "login", "@login" or a GitHub profile URL; null if it can't be one.</summary>
    public static string? NormalizeLogin(string? input)
    {
        var login = (input ?? "").Trim();
        if (Uri.TryCreate(login, UriKind.Absolute, out var uri) && uri.Host is "github.com" or "www.github.com")
        {
            login = uri.AbsolutePath.Trim('/');
        }

        login = login.TrimStart('@');
        return GitHubLogin().IsMatch(login) ? login.ToLowerInvariant() : null;
    }

    /// <summary>Letters, digits and single hyphens between them, up to 39 characters.</summary>
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$")]
    private static partial Regex GitHubLogin();
}

public sealed class StartTestingForm
{
    [Required(ErrorMessage = "Choose the date that stands in for December 1.")]
    public DateOnly? RehearsalStartsOn { get; set; }
}

public sealed class TesterForm
{
    [Required(ErrorMessage = "Enter a GitHub login.")]
    [StringLength(100)]
    public string Login { get; set; } = "";
}

public sealed class FinishTestingForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm that everything done in this season should be deleted.")]
    public bool Confirm { get; set; }
}
