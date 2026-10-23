using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

public sealed record RequestListItem(
    int Id,
    int Day,
    int Part,
    string DisplayName,
    string GitHubLogin,
    DateTimeOffset SubmittedAt,
    ClaimRequestStatus Status,
    int OtherPendingForSpot,
    int PastPosts = 0);

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
    IReadOnlyList<SpotStatus> OpenSpots);

public sealed record ClaimantSocials(string? Bluesky, string? Mastodon, string? LinkedIn, string? Facebook, string? Reddit)
{
    public bool Any => Bluesky is not null || Mastodon is not null || LinkedIn is not null || Facebook is not null || Reddit is not null;
}

public enum ReviewOutcome
{
    Approved,
    Archived,
    AlreadyDecided,
    SpotTaken,
    NotFound,
}

/// <summary>
/// The organizer's side of claims: list requests, and approve one into a spot or archive it. Approving copies the request
/// into the spot (author, link, contributor and contact email); other requests for that spot stay pending until archived.
/// </summary>
public sealed class ClaimReviewService(AdventDbContext db, TimeProvider timeProvider, IClaimNotifier notifier)
{
    /// <summary>A season's requests with the given status, oldest first: first come, first served.</summary>
    public Task<List<RequestListItem>> ListAsync(int year, ClaimRequestStatus status, CancellationToken cancellationToken = default) =>
        db.ClaimRequests
            .Where(r => r.Slot.SeasonYear == year && r.Status == status)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new RequestListItem(
                r.Id,
                r.Slot.Day,
                r.Slot.Part,
                r.DisplayName,
                r.Contributor.GithubLogin,
                r.SubmittedAt,
                r.Status,
                db.ClaimRequests.Count(other => other.SlotId == r.SlotId && other.Id != r.Id && other.Status == ClaimRequestStatus.Pending),
                // Posts they've published in earlier seasons, linked to their account (AuthorService).
                db.Slots.Count(s => s.ContributorId == r.ContributorId && s.SeasonYear < r.Slot.SeasonYear && s.SeasonYear != Season.TestYear
                    && s.PostUrl != null && s.AuthorName != null)))
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

        var others = await ListForSpotAsync(request.SlotId, request.Id, cancellationToken);
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
            new ClaimantSocials(contributor.BlueskyHandle, contributor.MastodonHandle, contributor.LinkedInUrl, contributor.FacebookUrl, contributor.RedditUsername),
            [.. spots.Where(s => s.Day == request.Slot.Day)],
            others,
            [.. spots.Where(s => s.AuthorName is null)]);
    }

    /// <summary>
    /// Approves a pending request into <paramref name="slotId"/>, which can be the requested spot or another open spot in the
    /// same season. Safe to repeat: a request that's already decided, or a spot that's been claimed, is reported, not changed.
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

        await notifier.ClaimApprovedAsync(request.Id, slot.SeasonYear, slot.Day, slot.Part, cancellationToken);
        return ReviewOutcome.Approved;
    }

    public async Task<ReviewOutcome> ArchiveAsync(int requestId, CancellationToken cancellationToken = default)
    {
        var request = await db.ClaimRequests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return ReviewOutcome.NotFound;
        }

        if (request.Status is not ClaimRequestStatus.Pending)
        {
            return ReviewOutcome.AlreadyDecided;
        }

        request.Status = ClaimRequestStatus.Archived;
        request.DecidedAt = timeProvider.GetUtcNow();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ReviewOutcome.AlreadyDecided;
        }

        return ReviewOutcome.Archived;
    }

    private Task<List<RequestListItem>> ListForSpotAsync(int slotId, int exceptId, CancellationToken cancellationToken) =>
        db.ClaimRequests
            .Where(r => r.SlotId == slotId && r.Id != exceptId)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => new RequestListItem(r.Id, r.Slot.Day, r.Slot.Part, r.DisplayName, r.Contributor.GithubLogin, r.SubmittedAt, r.Status, 0))
            .ToListAsync(cancellationToken);
}
