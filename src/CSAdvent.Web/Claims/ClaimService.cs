using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
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

/// <summary>
/// Claim requests: a signed-in contributor asks for an open slot and the organizer approves one request per slot. Requests
/// don't reserve the slot, so several people can ask for the same one; first come, first served.
/// </summary>
public sealed class ClaimService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, IClaimNotifier notifier)
{
    public static ClaimAvailability CheckAvailability(Season season, Slot slot, AdventSchedule schedule) =>
        !schedule.AreClaimsOpen(season.ClaimsOpenAt) ? ClaimAvailability.ClaimsNotOpen
        : schedule.IsRevealed(slot) ? ClaimAvailability.AlreadyRevealed
        : slot.IsClaimed ? ClaimAvailability.AlreadyClaimed
        : ClaimAvailability.Open;

    public async Task<ClaimableSlot?> FindAsync(int year, int day, int part, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots
            .AsNoTracking()
            .Include(s => s.Season)
            .SingleOrDefaultAsync(s => s.SeasonYear == year && s.Day == day && s.Part == part, cancellationToken);

        return slot is null
            ? null
            : new ClaimableSlot(slot.Id, year, day, part, slot.Season.SlotsPerDay, CheckAvailability(slot.Season, slot, schedule));
    }

    /// <summary>The contributor's verified email, which is where claim emails go. Null if GitHub hasn't verified one.</summary>
    public Task<string?> GetVerifiedEmailAsync(int contributorId, CancellationToken cancellationToken = default) =>
        db.Contributors
            .Where(c => c.Id == contributorId && c.EmailVerifiedAt != null)
            .Select(c => c.Email)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ClaimOutcome> SubmitAsync(int contributorId, int year, int day, int part, ClaimForm form, CancellationToken cancellationToken = default)
    {
        var email = await GetVerifiedEmailAsync(contributorId, cancellationToken);
        if (email is null)
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
}
