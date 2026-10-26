using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Claims;

/// <summary>Tells contributors and organizers about claim requests.</summary>
public interface IClaimNotifier
{
    /// <summary>A contributor asked for a spot: confirm to them and tell the organizers.</summary>
    Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken);

    /// <summary>
    /// An organizer approved a request into a spot: tell the contributor, and whether it's a different spot from the one they
    /// asked for (<paramref name="requestedDay"/> and <paramref name="requestedPart"/>).
    /// </summary>
    Task ClaimApprovedAsync(int claimRequestId, int year, int day, int part, int requestedDay, int requestedPart, CancellationToken cancellationToken);

    /// <summary>An organizer declined a request: tell the contributor why.</summary>
    Task ClaimDeclinedAsync(int claimRequestId, int year, int day, int part, ClaimDeclineReason reason, CancellationToken cancellationToken);
}

/// <summary>
/// Emails contributors and organizers about claim requests, through the email queue (#152). The request is already saved
/// when these run, so an email that can't be queued is logged (without personal details) and never undoes or blocks it.
/// </summary>
public sealed class EmailClaimNotifier(AdventDbContext db, EmailComposer composer, IOptions<SiteOptions> site) : IClaimNotifier
{
    public async Task ClaimRequestedAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken)
    {
        if (await LoadAsync(claimRequestId, year, day, part, cancellationToken) is not { } claim)
        {
            return;
        }

        var contributor = new EmailRecipient(claim.Email, claim.DisplayName);
        await composer.TrySendAsync(ClaimEmails.Received(claim, site.Value), contributor, About(claimRequestId), cancellationToken);

        // Organizers can reply straight to the contributor.
        await composer.TrySendAsync(ClaimEmails.NewRequest(claim, site.Value), site.Value.OrganizersInbox, claim.Email, About(claimRequestId), cancellationToken);
    }

    public async Task ClaimApprovedAsync(int claimRequestId, int year, int day, int part, int requestedDay, int requestedPart, CancellationToken cancellationToken)
    {
        if (await LoadAsync(claimRequestId, year, day, part, cancellationToken) is not { } claim)
        {
            return;
        }

        await composer.TrySendAsync(
            ClaimEmails.Approved(claim with { RequestedDay = requestedDay, RequestedPart = requestedPart }, site.Value),
            new EmailRecipient(claim.Email, claim.DisplayName),
            About(claimRequestId),
            cancellationToken);
    }

    public async Task ClaimDeclinedAsync(int claimRequestId, int year, int day, int part, ClaimDeclineReason reason, CancellationToken cancellationToken)
    {
        if (await LoadAsync(claimRequestId, year, day, part, cancellationToken) is not { } claim)
        {
            return;
        }

        await composer.TrySendAsync(
            ClaimEmails.Declined(claim, reason, site.Value),
            new EmailRecipient(claim.Email, claim.DisplayName),
            About(claimRequestId),
            cancellationToken);
    }

    /// <summary>The request's details, with the spot it's for (an organizer can approve it into a different one).</summary>
    private Task<ClaimEmailDetails?> LoadAsync(int claimRequestId, int year, int day, int part, CancellationToken cancellationToken) =>
        db.ClaimRequests
            .AsNoTracking()
            .Where(r => r.Id == claimRequestId)
            .Select(r => new ClaimEmailDetails(
                r.Id,
                r.DisplayName,
                r.Email,
                r.Contributor.GithubLogin,
                r.Link,
                r.Comments,
                year,
                day,
                part,
                r.Slot.Season.SlotsPerDay))
            .SingleOrDefaultAsync(cancellationToken);

    private static string About(int claimRequestId) => $"claim request {claimRequestId}";
}
