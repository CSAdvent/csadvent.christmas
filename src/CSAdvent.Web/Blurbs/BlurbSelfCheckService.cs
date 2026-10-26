using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Blurbs;

public enum SelfCheckOutcome
{
    /// <summary>Checked, or it didn't need checking (it passed, or an organizer decided): the spot's blurb check says where it stands.</summary>
    Checked,

    /// <summary>They've asked <see cref="BlurbSelfCheckService.MaxPerHour"/> times in the last hour.</summary>
    TooMany,

    /// <summary>Checks on request have had their share of Claude for now (<see cref="BlurbOptions.SelfChecksPerMonth"/>).</summary>
    Paused,

    /// <summary>There's nothing to check: no post link, a season without the rule, a day that's over, or the check is off.</summary>
    NotAvailable,

    /// <summary>It isn't their spot.</summary>
    NotFound,
}

/// <summary>
/// "Check my post" on My spots (#174), and the check when an author saves a new link: the author's own blurb check, whenever
/// they like before their day is over, so they hear about a missing blurb, or a page we can't read, days before it matters.
/// Before the reveal it's only information (<see cref="BlurbCheckService.CheckForAuthorAsync"/>). Each person can ask
/// <see cref="MaxPerHour"/> times an hour, and the checks that go to Claude are capped across everyone
/// (<see cref="BlurbOptions.SelfChecksPerMonth"/>), so they can't use up the Anthropic spend limit December's checks need.
/// </summary>
public sealed partial class BlurbSelfCheckService(
    AdventDbContext db,
    MySpotsService mySpots,
    BlurbCheckService blurbs,
    TimeProvider timeProvider,
    IOptions<BlurbOptions> options,
    ILogger<BlurbSelfCheckService> logger)
{
    /// <summary>Checks one person can ask for in an hour, whatever they cost.</summary>
    public const int MaxPerHour = 10;

    /// <summary>The window <see cref="BlurbOptions.SelfChecksPerMonth"/> counts over.</summary>
    public static readonly TimeSpan Month = TimeSpan.FromDays(30);

    public bool IsEnabled => blurbs.IsEnabled;

    /// <summary>Checks the contributor's own spot's post now, if they may.</summary>
    public async Task<SelfCheckOutcome> CheckAsync(int contributorId, int slotId, CancellationToken cancellationToken = default)
    {
        if (!blurbs.IsEnabled)
        {
            return SelfCheckOutcome.NotAvailable;
        }

        // Only their own spots, as My spots shows them (seasons that aren't over).
        if (await mySpots.GetAsync(contributorId, slotId, cancellationToken) is not { } spot)
        {
            return SelfCheckOutcome.NotFound;
        }

        if (spot.Values.PostUrl is null || spot.IsDayOver || !BlurbRules.AppliesTo(spot.Year))
        {
            return SelfCheckOutcome.NotAvailable;
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            var hourAgo = now.AddHours(-1);
            if (await db.BlurbSelfChecks.CountAsync(c => c.ContributorId == contributorId && c.CheckedAt > hourAgo, cancellationToken) >= MaxPerHour)
            {
                return SelfCheckOutcome.TooMany;
            }

            var monthAgo = now - Month;
            if (await db.BlurbSelfChecks.CountAsync(c => c.AskedClaude && c.CheckedAt > monthAgo, cancellationToken) >= options.Value.SelfChecksPerMonth)
            {
                LogPaused(options.Value.SelfChecksPerMonth);
                return SelfCheckOutcome.Paused;
            }
        }
        catch (Exception exception) when (MissingSchema.IsMissingTableOrColumn(exception))
        {
            // The minute between a deploy and its migrations: no limits to go by yet, so no checks either.
            return SelfCheckOutcome.Paused;
        }

        // Only a check that fetched the post counts: one that passed, or that an organizer decided, isn't checked again.
        var run = await blurbs.CheckForAuthorAsync(slotId, now, cancellationToken);
        if (run.Checked > 0)
        {
            db.BlurbSelfChecks.Add(new BlurbSelfCheck { SlotId = slotId, ContributorId = contributorId, CheckedAt = now, AskedClaude = run.AskedClaude > 0 });
            await db.SaveChangesAsync(cancellationToken);
        }

        return SelfCheckOutcome.Checked;
    }

    /// <summary>Whether saving a spot gave it a new post link, which is then checked straight away.</summary>
    public static bool LinkChanged(string? before, string? after)
    {
        var link = string.IsNullOrWhiteSpace(after) ? null : after.Trim();
        return link is not null && link != before;
    }

    /// <summary>What the pages put in the address after a check, so the spot's box can say how it went.</summary>
    public static string QueryValue(SelfCheckOutcome outcome) => outcome switch
    {
        SelfCheckOutcome.Checked => "checked",
        SelfCheckOutcome.TooMany => "too-many",
        SelfCheckOutcome.Paused => "paused",
        _ => "unavailable",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checks on request are paused: {Limit} have gone to Claude in the last 30 days")]
    private partial void LogPaused(int limit);
}
