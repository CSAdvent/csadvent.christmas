using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Blurbs;

/// <summary>A post's blurb, as the pages show it, with Claude's last answer.</summary>
public sealed record BlurbState(
    BlurbStatus Status,
    string? Detail,
    DateTimeOffset StatusSince,
    DateTimeOffset? CheckedAt,
    string? DecidedBy,
    bool BackupRequested,
    string? Quote = null,
    string? Reason = null)
{
    /// <summary>Whether its post can be announced automatically: once it passes, or an organizer lets it through.</summary>
    public bool CanAnnounce => Status is BlurbStatus.Passed or BlurbStatus.Waived;

    /// <summary>"Not checked yet" until a check has run, then the status (a post waiting to be published is "Waiting").</summary>
    public string Label => Status is BlurbStatus.Waiting && CheckedAt is null ? Labels.NotChecked : Labels.For(Status);
}

/// <summary>What the organizer pages say about each status.</summary>
public static class Labels
{
    public const string NotChecked = "Not checked yet";

    public static string For(BlurbStatus status) => status switch
    {
        BlurbStatus.Waiting => "Waiting",
        BlurbStatus.Passed => "Blurb passed",
        BlurbStatus.Missing => "Missing blurb",
        BlurbStatus.NeedsDecision => "Needs a decision",
        BlurbStatus.CouldNotCheck => "Couldn't check",
        _ => "Announced anyway",
    };

    /// <summary>Full class names, so Tailwind generates them.</summary>
    public static string Badge(BlurbStatus status) => status switch
    {
        BlurbStatus.Passed => "bg-green-100 text-green-800",
        BlurbStatus.Missing => "bg-red-100 text-red-800",
        BlurbStatus.NeedsDecision or BlurbStatus.CouldNotCheck => "bg-amber-100 text-amber-800",
        _ => "bg-slate-100 text-slate-600",
    };
}

/// <summary>A post's blurb check in full, for its entry page: whether it applies, the status, and the deadline.</summary>
/// <param name="StandingBy">For a missing blurb, the backup standing by to take the spot (#148).</param>
public sealed record BlurbDetails(int SlotId, bool Applies, bool Enabled, bool HasPost, BlurbState? State, DateTimeOffset Deadline, BlurbBackup? StandingBy = null);

/// <summary>What the pages read about blurb checks.</summary>
public sealed class BlurbQueries(AdventDbContext db, AdventSchedule schedule, IOptions<BlurbOptions> options, IOptions<SocialPostingOptions> posting)
{
    public bool IsEnabled => options.Value.IsEnabled;

    /// <summary>Each spot's blurb in a season, for spots whose current post has been looked at. Empty when the rule doesn't apply.</summary>
    public async Task<Dictionary<int, BlurbState>> GetSeasonAsync(int year, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !BlurbRules.AppliesTo(year))
        {
            return [];
        }

        var checks = await db.BlurbChecks
            .AsNoTracking()
            .Where(c => c.Slot.SeasonYear == year && c.PostUrl == c.Slot.PostUrl)
            .ToListAsync(cancellationToken);
        return checks.ToDictionary(c => c.SlotId, State);
    }

    /// <summary>The spots' blurbs, for a few spots (their own, on My spots).</summary>
    public async Task<Dictionary<int, BlurbState>> GetSlotsAsync(IReadOnlyCollection<int> slotIds, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || slotIds.Count == 0)
        {
            return [];
        }

        var checks = await db.BlurbChecks
            .AsNoTracking()
            .Where(c => slotIds.Contains(c.SlotId) && c.PostUrl == c.Slot.PostUrl && c.Slot.SeasonYear >= BlurbRules.FirstYear)
            .ToListAsync(cancellationToken);
        return checks.ToDictionary(c => c.SlotId, State);
    }

    public async Task<BlurbDetails?> GetAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots.AsNoTracking().Where(s => s.Id == slotId)
            .Select(s => new { s.Id, s.SeasonYear, s.Day, s.Part, s.PostUrl })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is null)
        {
            return null;
        }

        var check = await db.BlurbChecks.AsNoTracking().SingleOrDefaultAsync(c => c.SlotId == slotId && c.PostUrl == slot.PostUrl, cancellationToken);
        var standingBy = check?.Status is BlurbStatus.Missing
            ? await db.BackupOffers
                .AsNoTracking()
                .Where(o => o.SlotId == slotId && o.Status == BackupOfferStatus.Accepted && o.PostUrl != null)
                .Select(o => new BlurbBackup(o.AuthorName ?? o.Substitute.Name, o.PostTitle, o.PostUrl!))
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new BlurbDetails(
            slotId,
            BlurbRules.AppliesTo(slot.SeasonYear),
            IsEnabled,
            slot.PostUrl is not null,
            check is null ? null : State(check),
            BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part),
            standingBy);
    }

    private static BlurbState State(BlurbCheck check) =>
        new(check.Status, check.Detail, check.StatusSince, check.CheckedAt, check.DecidedBy, check.BackupRequestedAt is not null, check.Quote, check.Reason);
}
