using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

/// <param name="PastPosts">Posts they've published in earlier seasons, linked to their account.</param>
/// <param name="SpotTaken">Someone has the spot: for a pending request, someone else.</param>
public sealed record RequestListItem(
    int Id,
    int SlotId,
    int Day,
    int Part,
    string DisplayName,
    string GitHubLogin,
    DateTimeOffset SubmittedAt,
    ClaimRequestStatus Status,
    int OtherPendingForSpot,
    int PastPosts,
    bool SpotTaken,
    string? Comments)
{
    /// <summary>Waiting, nobody else wants its spot, and the spot is open, so it can be approved straight from the list (#202).</summary>
    public bool CanApproveFromList => Status is ClaimRequestStatus.Pending && OtherPendingForSpot == 0 && !SpotTaken;
}

public sealed record SpotStatus(int SlotId, int Day, int Part, string? AuthorName);

public sealed record RequestDetails(
    int Id,
    int Year,
    int SlotsPerDay,
    SpotStatus RequestedSpot,
    string DisplayName,
    string Email,
    string? Link,
    string? Comments,
    DateTimeOffset SubmittedAt,
    ClaimRequestStatus Status,
    DateTimeOffset? DecidedAt,
    string GitHubLogin,
    ClaimantSocials Socials,
    IReadOnlyList<SpotStatus> SameDay,
    IReadOnlyList<RequestListItem> OtherRequestsForSpot,
    IReadOnlyList<SpotStatus> OpenSpots,
    int? NextPendingId)
{
    /// <summary>The other requests still waiting for this one's spot: once it's approved, they need settling (#143).</summary>
    public IReadOnlyList<RequestListItem> Competing => [.. OtherRequestsForSpot.Where(r => r.Status is ClaimRequestStatus.Pending)];
}

public sealed record ClaimantSocials(string? Bluesky, string? Mastodon, string? LinkedIn, string? Facebook, string? Reddit, string? X)
{
    public bool Any => Bluesky is not null || Mastodon is not null || LinkedIn is not null || Facebook is not null || Reddit is not null || X is not null;
}

public enum ReviewOutcome
{
    Approved,
    Declined,
    AlreadyDecided,
    SpotTaken,
    NotFound,
}

/// <summary>What the review pages say when an approve or decline doesn't go through. The plain forms pass it on as <c>?error=</c>.</summary>
public static class ReviewMessages
{
    public const string ChooseSpot = "choose-spot";

    public static string Code(ReviewOutcome outcome) => outcome switch
    {
        ReviewOutcome.SpotTaken => "spot-taken",
        ReviewOutcome.AlreadyDecided => "already-decided",
        _ => "not-found",
    };

    public static string? Describe(string? code) => code switch
    {
        "spot-taken" => "That spot has just been claimed. Choose another open spot.",
        "already-decided" => "That request has already been approved, declined or withdrawn.",
        ChooseSpot => "Choose the spot to approve them into.",
        "not-found" => "That spot doesn't exist in this season.",
        _ => null,
    };
}

/// <summary>
/// The organizer's side of claims: list requests, and approve one into a spot or decline it. Approving copies the request
/// into the spot (author, link, contributor and contact email), always the spot the organizer chose. The other requests for
/// that spot stay pending until they're settled: approved into another open spot, or declined (#143). The contributor is
/// emailed either way.
/// </summary>
public sealed class ClaimReviewService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, IClaimNotifier notifier)
{
    /// <summary>A season's requests with the given status, oldest first: first come, first served.</summary>
    public Task<List<RequestListItem>> ListAsync(int year, ClaimRequestStatus status, CancellationToken cancellationToken = default) =>
        Items(db.ClaimRequests.Where(r => r.Slot.SeasonYear == year && r.Status == status).OrderBy(r => r.SubmittedAt))
            .ToListAsync(cancellationToken);

    public async Task<RequestDetails?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var request = await db.ClaimRequests
            .AsNoTracking()
            .Include(r => r.Slot).ThenInclude(s => s.Season)
            .Include(r => r.Contributor)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (request is null)
        {
            return null;
        }

        var year = request.Slot.SeasonYear;
        var spots = await db.Slots
            .Where(s => s.SeasonYear == year)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new SpotStatus(s.Id, s.Day, s.Part, s.AuthorName))
            .ToListAsync(cancellationToken);

        var others = await Items(db.ClaimRequests.Where(r => r.SlotId == request.SlotId && r.Id != request.Id).OrderBy(r => r.SubmittedAt))
            .ToListAsync(cancellationToken);
        var contributor = request.Contributor;

        return new RequestDetails(
            request.Id,
            year,
            request.Slot.Season.SlotsPerDay,
            spots.Single(s => s.SlotId == request.SlotId),
            request.DisplayName,
            request.Email,
            request.Link,
            request.Comments,
            request.SubmittedAt,
            request.Status,
            request.DecidedAt,
            contributor.GithubLogin,
            new ClaimantSocials(contributor.BlueskyHandle, contributor.MastodonHandle, contributor.LinkedInUrl, contributor.FacebookUrl, contributor.RedditUsername, contributor.XUsername),
            [.. spots.Where(s => s.Day == request.Slot.Day)],
            others,
            [.. spots.Where(s => s.AuthorName is null)],
            await NextPendingAsync(year, request.Id, cancellationToken));
    }

    /// <summary>The oldest request still waiting in the season, other than <paramref name="exceptId"/>: "Next pending request" (#202).</summary>
    public Task<int?> NextPendingAsync(int year, int exceptId, CancellationToken cancellationToken = default) =>
        db.ClaimRequests
            .Where(r => r.Slot.SeasonYear == year && r.Status == ClaimRequestStatus.Pending && r.Id != exceptId)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Whether other requests are still waiting for the spot this one has.</summary>
    public Task<bool> HasCompetingAsync(int requestId, CancellationToken cancellationToken = default) =>
        db.ClaimRequests.AnyAsync(
            r => r.Id != requestId && r.Status == ClaimRequestStatus.Pending && db.ClaimRequests.Any(mine => mine.Id == requestId && mine.SlotId == r.SlotId),
            cancellationToken);

    /// <summary>
    /// Approves a pending request into <paramref name="slotId"/>, which the organizer chose: the requested spot or another open
    /// spot in the same season, which the email to the contributor points out. Safe to repeat: a request that's already
    /// decided, or a spot that's been claimed, is reported, not changed.
    /// </summary>
    public async Task<ReviewOutcome> ApproveAsync(int requestId, int slotId, CancellationToken cancellationToken = default)
    {
        var request = await db.ClaimRequests.Include(r => r.Slot).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return ReviewOutcome.NotFound;
        }

        if (request.Status is not ClaimRequestStatus.Pending)
        {
            return ReviewOutcome.AlreadyDecided;
        }

        var slot = await db.Slots
            .Include(s => s.Contact)
            .SingleOrDefaultAsync(s => s.Id == slotId && s.SeasonYear == request.Slot.SeasonYear, cancellationToken);
        if (slot is null)
        {
            return ReviewOutcome.NotFound;
        }

        if (slot.IsClaimed)
        {
            return ReviewOutcome.SpotTaken;
        }

        var (requestedDay, requestedPart) = (request.Slot.Day, request.Slot.Part);
        slot.AuthorName = request.DisplayName;
        slot.AuthorUrl = request.Link;
        slot.ContributorId = request.ContributorId;
        if (slot.Contact is null)
        {
            slot.Contact = new SlotContact { Email = request.Email };
        }
        else
        {
            slot.Contact.Email = request.Email;
        }

        request.SlotId = slot.Id;
        request.Status = ClaimRequestStatus.Approved;
        request.DecidedAt = timeProvider.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed the spot or the request at the same moment (xmin); nothing was saved.
            return ReviewOutcome.SpotTaken;
        }

        await notifier.ClaimApprovedAsync(request.Id, slot.SeasonYear, slot.Day, slot.Part, requestedDay, requestedPart, cancellationToken);
        return ReviewOutcome.Approved;
    }

    /// <summary>
    /// Declines a pending request and tells the contributor: that their spot has gone to someone else (with a link to the open
    /// days, or to the backup list once every spot is taken), or that it wasn't approved. Safe to repeat, like approving.
    /// </summary>
    public async Task<ReviewOutcome> DeclineAsync(int requestId, CancellationToken cancellationToken = default)
    {
        var request = await db.ClaimRequests.Include(r => r.Slot).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return ReviewOutcome.NotFound;
        }

        if (request.Status is not ClaimRequestStatus.Pending)
        {
            return ReviewOutcome.AlreadyDecided;
        }

        request.Status = ClaimRequestStatus.Declined;
        request.DecidedAt = timeProvider.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Decided or withdrawn at the same moment (xmin); nothing was saved.
            return ReviewOutcome.AlreadyDecided;
        }

        var slot = request.Slot;
        await notifier.ClaimDeclinedAsync(request.Id, slot.SeasonYear, slot.Day, slot.Part, await DeclineReasonAsync(slot, cancellationToken), cancellationToken);
        return ReviewOutcome.Declined;
    }

    /// <summary>
    /// Declines every request still waiting for the spot an approved request got, each with the "it's gone" email. Returns
    /// how many were declined.
    /// </summary>
    public async Task<int> DeclineCompetingAsync(int approvedRequestId, CancellationToken cancellationToken = default)
    {
        var slotId = await db.ClaimRequests
            .Where(r => r.Id == approvedRequestId && r.Status == ClaimRequestStatus.Approved)
            .Select(r => (int?)r.SlotId)
            .SingleOrDefaultAsync(cancellationToken);
        if (slotId is null)
        {
            return 0;
        }

        var competing = await db.ClaimRequests
            .Where(r => r.SlotId == slotId && r.Status == ClaimRequestStatus.Pending)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var declined = 0;
        foreach (var id in competing)
        {
            if (await DeclineAsync(id, cancellationToken) is ReviewOutcome.Declined)
            {
                declined++;
            }
        }

        return declined;
    }

    /// <summary>Why the contributor's request wasn't approved: their spot is someone else's now, or the organizers said no.</summary>
    private async Task<ClaimDeclineReason> DeclineReasonAsync(Slot slot, CancellationToken cancellationToken)
    {
        if (!slot.IsClaimed)
        {
            return ClaimDeclineReason.NotApproved;
        }

        // Only days that aren't revealed yet can still be requested.
        var openDays = await db.Slots
            .Where(s => s.SeasonYear == slot.SeasonYear && s.AuthorName == null)
            .Select(s => s.Day)
            .ToListAsync(cancellationToken);
        return openDays.Any(day => !schedule.IsRevealed(slot.SeasonYear, day)) ? ClaimDeclineReason.SpotTaken : ClaimDeclineReason.SeasonFull;
    }

    // Filter and sort the requests before this: EF can't translate a filter on the record it projects to.
    private IQueryable<RequestListItem> Items(IQueryable<ClaimRequest> requests) =>
        requests.Select(r => new RequestListItem(
            r.Id,
            r.SlotId,
            r.Slot.Day,
            r.Slot.Part,
            r.DisplayName,
            r.Contributor.GithubLogin,
            r.SubmittedAt,
            r.Status,
            db.ClaimRequests.Count(other => other.SlotId == r.SlotId && other.Id != r.Id && other.Status == ClaimRequestStatus.Pending),
            // Posts they've published in earlier seasons, linked to their account (AuthorService).
            db.Slots.Count(s => s.ContributorId == r.ContributorId && s.SeasonYear < r.Slot.SeasonYear && s.SeasonYear != Season.TestYear
                && s.PostUrl != null && s.AuthorName != null),
            r.Slot.AuthorName != null,
            r.Comments));
}
