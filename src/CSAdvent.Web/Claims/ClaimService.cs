using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Claims;

public enum ClaimAvailability
{
    Open,
    ClaimsNotOpen,
    AlreadyClaimed,
    AlreadyRevealed,
}

public enum ClaimOutcome
{
    Submitted,
    AlreadyRequested,
    NoVerifiedEmail,
    Unavailable,
}

public sealed record ClaimableSlot(int SlotId, int Year, int Day, int Part, int SlotsPerDay, ClaimAvailability Availability);

/// <summary>What the claim form shows and prefills for the signed-in contributor.</summary>
public sealed record ClaimantDetails(string? VerifiedEmail, string? Bluesky, string? Mastodon, string? LinkedIn, string? Facebook, string? Reddit, string? X, string? DisplayName, string? WebsiteUrl);

/// <summary>
/// Claim requests: a signed-in contributor asks for an open slot and the organizer approves one request per slot. Requests
/// don't reserve the slot, so several people can ask for the same one; first come, first served.
/// </summary>
public sealed class ClaimService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, IClaimNotifier notifier, SeasonAccess access)
{
    /// <summary>Whether the slot can be requested. The test season is always open, to the testers who can see it.</summary>
    public static ClaimAvailability CheckAvailability(Season season, Slot slot, AdventSchedule schedule) =>
        !(season.IsTest || schedule.AreClaimsOpen(season.ClaimsOpenAt)) ? ClaimAvailability.ClaimsNotOpen
        : schedule.IsRevealed(slot) ? ClaimAvailability.AlreadyRevealed
        : slot.IsClaimed ? ClaimAvailability.AlreadyClaimed
        : ClaimAvailability.Open;

    public async Task<ClaimableSlot?> FindAsync(int year, int day, int part, CancellationToken cancellationToken = default)
    {
        if (!access.CanSee(year))
        {
            return null;
        }

        var slot = await db.Slots
            .AsNoTracking()
            .Include(s => s.Season)
            .SingleOrDefaultAsync(s => s.SeasonYear == year && s.Day == day && s.Part == part, cancellationToken);

        return slot is null
            ? null
            : new ClaimableSlot(slot.Id, year, day, part, slot.Season.SlotsPerDay, CheckAvailability(slot.Season, slot, schedule));
    }

    /// <summary>
    /// The contributor's verified email, where claim emails go (null if GitHub hasn't verified one), and the name, link and
    /// social accounts they gave before (on their profile or their last claim).
    /// </summary>
    public Task<ClaimantDetails?> GetClaimantAsync(int contributorId, CancellationToken cancellationToken = default) =>
        db.Contributors
            .Where(c => c.Id == contributorId)
            .Select(c => new ClaimantDetails(
                c.EmailVerifiedAt != null ? c.Email : null,
                c.BlueskyHandle,
                c.MastodonHandle,
                c.LinkedInUrl,
                c.FacebookUrl,
                c.RedditUsername,
                c.XUsername,
                c.DisplayName,
                c.WebsiteUrl))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimOutcome> SubmitAsync(int contributorId, int year, int day, int part, ClaimForm form, CancellationToken cancellationToken = default)
    {
        var contributor = await db.Contributors.SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken);
        if (contributor is not { Email: { } email, EmailVerifiedAt: not null })
        {
            return ClaimOutcome.NoVerifiedEmail;
        }

        // Check again on submit: the slot may have been claimed, or revealed, since the form was shown.
        var slot = await FindAsync(year, day, part, cancellationToken);
        if (slot is not { Availability: ClaimAvailability.Open })
        {
            return ClaimOutcome.Unavailable;
        }

        var now = timeProvider.GetUtcNow();
        var request = new ClaimRequest
        {
            SlotId = slot.SlotId,
            ContributorId = contributorId,
            DisplayName = form.DisplayName.Trim(),
            Email = email,
            Link = string.IsNullOrWhiteSpace(form.Link) ? null : form.Link.Trim(),
            Comments = string.IsNullOrWhiteSpace(form.Comments) ? null : form.Comments.Trim(),
            TermsAcceptedAt = now,
            TimingAcceptedAt = now,
            SubmittedAt = now,
            Status = ClaimRequestStatus.Pending,
        };

        db.ClaimRequests.Add(request);

        // Saved on the contributor, so they're prefilled next time and show on their profile. The form has already validated them.
        contributor.DisplayName = form.DisplayName.Trim();
        contributor.WebsiteUrl = string.IsNullOrWhiteSpace(form.Link) ? null : form.Link.Trim();
        contributor.BlueskyHandle = Normalize(SocialPlatform.Bluesky, form.Bluesky);
        contributor.MastodonHandle = Normalize(SocialPlatform.Mastodon, form.Mastodon);
        contributor.LinkedInUrl = Normalize(SocialPlatform.LinkedIn, form.LinkedIn);
        contributor.FacebookUrl = Normalize(SocialPlatform.Facebook, form.Facebook);
        contributor.RedditUsername = Normalize(SocialPlatform.Reddit, form.Reddit);
        contributor.XUsername = Normalize(SocialPlatform.X, form.X);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The unique index on pending requests: this contributor already asked for this slot.
            return ClaimOutcome.AlreadyRequested;
        }

        await notifier.ClaimRequestedAsync(request.Id, year, day, part, cancellationToken);
        return ClaimOutcome.Submitted;
    }

    private static string? Normalize(SocialPlatform platform, string? input) =>
        SocialAccounts.TryNormalize(platform, input, out var normalized)
            ? normalized
            : throw new ArgumentException($"'{input}' isn't a valid {platform} account.", nameof(input));
}
