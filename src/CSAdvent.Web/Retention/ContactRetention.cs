using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Retention;

public sealed record ContactDeletion(int SpotContacts, int ClaimRequests, int Substitutes, int ContributorEmails, int PostClaimRequests = 0)
{
    public static readonly ContactDeletion None = new(0, 0, 0, 0, 0);

    public bool DeletedAnything => this != None;
}

/// <summary>
/// Keeps the privacy page's retention promise. Once a season's deletion time has passed (February 1 after it), its contact
/// details are deleted: spot emails, claim requests and the standby list, plus every contributor email that wasn't verified
/// again after that time. The public calendar is left as it is. Requests to add an older post to someone's account go
/// <see cref="PostClaimsKeptFor"/> after an organizer decides them, whatever the season. Deleting is idempotent, so it's safe
/// to run often and from every replica.
/// </summary>
public sealed class ContactRetention(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, ILogger<ContactRetention> logger)
{
    /// <summary>How long a decided request to add an older post to someone's account is kept.</summary>
    public static readonly TimeSpan PostClaimsKeptFor = TimeSpan.FromDays(30);

    public async Task<ContactDeletion> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var decidedBefore = now - PostClaimsKeptFor;
        var postClaimRequests = await db.PostClaimRequests
            .Where(r => r.DecidedAt != null && r.DecidedAt < decidedBefore)
            .ExecuteDeleteAsync(cancellationToken);

        // Deletion times depend on the reveal time zone, so they're worked out here rather than in SQL. There's one row per year.
        var years = await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken);
        var expired = years.Where(year => schedule.GetContactDeletionTime(year) <= now).ToList();
        if (expired.Count == 0)
        {
            return Logged(ContactDeletion.None with { PostClaimRequests = postClaimRequests });
        }

        var spotContacts = await db.SlotContacts
            .Where(c => expired.Contains(c.Slot.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);
        var claimRequests = await db.ClaimRequests
            .Where(r => expired.Contains(r.Slot.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);
        var substitutes = await db.Substitutes
            .Where(s => expired.Contains(s.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);

        // Every sign-in stores a freshly verified email, so anyone who has signed in since the latest deletion time keeps theirs.
        var cutoff = schedule.GetContactDeletionTime(expired.Max());
        var contributorEmails = await db.Contributors
            .Where(c => c.Email != null && (c.EmailVerifiedAt == null || c.EmailVerifiedAt < cutoff))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Email, (string?)null)
                    .SetProperty(c => c.EmailVerifiedAt, (DateTimeOffset?)null),
                cancellationToken);

        return Logged(new ContactDeletion(spotContacts, claimRequests, substitutes, contributorEmails, postClaimRequests));
    }

    private ContactDeletion Logged(ContactDeletion deletion)
    {
        if (deletion.DeletedAnything)
        {
            logger.LogInformation(
                "Deleted expired contact details: {SpotContacts} spot emails, {ClaimRequests} claim requests, {Substitutes} substitutes, {ContributorEmails} contributor emails and {PostClaimRequests} post claim requests.",
                deletion.SpotContacts,
                deletion.ClaimRequests,
                deletion.Substitutes,
                deletion.ContributorEmails,
                deletion.PostClaimRequests);
        }

        return deletion;
    }
}
