using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Retention;

public sealed record ContactDeletion(int ClaimRequests, int Substitutes, int ContributorEmails, int PostClaimRequests = 0, int PostLinkRequests = 0, int PastAuthorInvitations = 0)
{
    public static readonly ContactDeletion None = new(0, 0, 0, 0, 0, 0);

    public bool DeletedAnything => this != None;
}

/// <summary>
/// Keeps the privacy page's retention promise. Once a season's deletion time has passed (February 1 after it), its claim
/// requests (with their comments) and its backup list are deleted, and so is the email of every contributor who has never had
/// a spot and hasn't signed in since. <b>Authors keep their email addresses</b>, on their account and on their spots, so they
/// can hear about the next season. The public calendar is left as it is. Requests to add an older post to someone's account
/// go <see cref="PostClaimsKeptFor"/> after an organizer decides them, and requests for new links to someone's older posts go
/// <see cref="PostLinkRequestsKeptFor"/> after they're decided or withdrawn, whatever the season. Deleting is idempotent, so
/// it's safe to run often and from every replica.
/// </summary>
public sealed class ContactRetention(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, ILogger<ContactRetention> logger)
{
    /// <summary>How long a decided request to add an older post to someone's account is kept.</summary>
    public static readonly TimeSpan PostClaimsKeptFor = TimeSpan.FromDays(30);

    /// <summary>How long a decided or withdrawn request for new links to someone's older posts is kept.</summary>
    public static readonly TimeSpan PostLinkRequestsKeptFor = TimeSpan.FromDays(30);

    public async Task<ContactDeletion> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        var decidedBefore = now - PostClaimsKeptFor;
        var postClaimRequests = await db.PostClaimRequests
            .Where(r => r.DecidedAt != null && r.DecidedAt < decidedBefore)
            .ExecuteDeleteAsync(cancellationToken);

        // Their changes go with them (a cascading delete).
        var linksDecidedBefore = now - PostLinkRequestsKeptFor;
        var postLinkRequests = await db.PostLinkRequests
            .Where(r => r.DecidedAt != null && r.DecidedAt < linksDecidedBefore)
            .ExecuteDeleteAsync(cancellationToken);

        // Deletion times depend on the reveal time zone, so they're worked out here rather than in SQL. There's one row per year.
        var years = await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken);
        var expired = years.Where(year => schedule.GetContactDeletionTime(year) <= now).ToList();
        if (expired.Count == 0)
        {
            return Logged(ContactDeletion.None with { PostClaimRequests = postClaimRequests, PostLinkRequests = postLinkRequests });
        }

        var claimRequests = await db.ClaimRequests
            .Where(r => expired.Contains(r.Slot.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);
        var substitutes = await db.Substitutes
            .Where(s => expired.Contains(s.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);
        await db.BackupSignupEmails
            .Where(e => expired.Contains(e.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);
        var invitations = await db.PastAuthorInvitations
            .Where(i => expired.Contains(i.SeasonYear))
            .ExecuteDeleteAsync(cancellationToken);

        // Every sign-in stores a freshly verified email, so anyone who has signed in since the latest deletion time keeps theirs,
        // and so does anyone who has had a spot: authors are told about the next season.
        var cutoff = schedule.GetContactDeletionTime(expired.Max());
        var contributorEmails = await db.Contributors
            .Where(c => c.Email != null
                && (c.EmailVerifiedAt == null || c.EmailVerifiedAt < cutoff)
                && !db.Slots.Any(s => s.ContributorId == c.Id && s.SeasonYear != Season.TestYear))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Email, (string?)null)
                    .SetProperty(c => c.EmailVerifiedAt, (DateTimeOffset?)null),
                cancellationToken);

        return Logged(new ContactDeletion(claimRequests, substitutes, contributorEmails, postClaimRequests, postLinkRequests, invitations));
    }

    private ContactDeletion Logged(ContactDeletion deletion)
    {
        if (deletion.DeletedAnything)
        {
            logger.LogInformation(
                "Deleted expired contact details: {ClaimRequests} claim requests, {Substitutes} substitutes, {ContributorEmails} contributor emails, {PostClaimRequests} post claim requests, {PostLinkRequests} post link requests and {PastAuthorInvitations} past author invitations.",
                deletion.ClaimRequests,
                deletion.Substitutes,
                deletion.ContributorEmails,
                deletion.PostClaimRequests,
                deletion.PostLinkRequests,
                deletion.PastAuthorInvitations);
        }

        return deletion;
    }
}
