using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Announcements;

/// <summary>A spot in a finished season, with the addresses we've kept for its author.</summary>
/// <param name="ContributorEmail">The verified email of the account the spot is linked to, if any.</param>
/// <param name="ContributorName">That account's profile name, if they've set one.</param>
/// <param name="ContactEmail">The spot's contact email, if it still has one (spots an organizer added by hand only have this).</param>
public sealed record PastSpot(int SlotId, int Year, bool HasPost, string AuthorName, string? ContributorEmail, string? ContributorName, string? ContactEmail);

/// <summary>Someone to invite back: one per address, however many seasons they wrote for.</summary>
public sealed record PastAuthor(string Address, string Name, int Posts, int FirstYear);

/// <summary>Where a season's invitations stand, for the organizers.</summary>
/// <param name="Waiting">Past authors who'd get one now (or still will).</param>
public sealed record InvitationStatus(int Year, DateTimeOffset? OpensAt, int Waiting, int Sent);

/// <summary>
/// Invites everyone who has written for C# Advent before when a season's sign-ups open (#133). It runs inside
/// <see cref="AnnouncementService.RunAsync"/>, at the same moment as the "sign-ups are open" announcement, and shares its
/// hourly allowance. Each invitation is recorded (by the address's fingerprint) before it's sent, so nobody gets two.
/// </summary>
/// <remarks>
/// Past authors are the authors of spots in finished seasons, at the addresses we keep for them: the verified email on the
/// linked account, and the spot's contact email. Skipped: confirmed announcement subscribers (they get the announcement the
/// same morning), anyone with a spot or a pending request in the new season, and anyone who unsubscribed from optional
/// emails. The invitation is optional, so it carries the unsubscribe link.
/// </remarks>
public sealed partial class PastAuthorInvitationService(
    AdventDbContext db,
    AdventSchedule schedule,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<ReminderOptions> reminders,
    IOptions<SiteOptions> site,
    TimeProvider timeProvider,
    ILogger<PastAuthorInvitationService> logger)
{
    /// <summary>The past authors who'd be invited to the season now: not yet invited, and not skipped for any reason.</summary>
    public async Task<IReadOnlyList<PastAuthor>> GetRecipientsAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear)
        {
            return [];
        }

        var spots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear < year && s.SeasonYear != Season.TestYear && s.AuthorName != null)
            .Select(s => new PastSpot(
                s.Id,
                s.SeasonYear,
                s.PostUrl != null,
                s.AuthorName!,
                s.Contributor != null && s.Contributor.EmailVerifiedAt != null ? s.Contributor.Email : null,
                s.Contributor != null ? s.Contributor.DisplayName : null,
                s.Contact != null ? s.Contact.Email : null))
            .ToListAsync(cancellationToken);

        // Their addresses in the new season: a spot (linked or by contact email), or a pending request.
        var taking = await db.Slots
            .Where(s => s.SeasonYear == year && s.AuthorName != null)
            .Select(s => new { Account = s.Contributor != null ? s.Contributor.Email : null, Contact = s.Contact != null ? s.Contact.Email : null })
            .ToListAsync(cancellationToken);
        var requesting = await db.ClaimRequests
            .Where(r => r.Slot.SeasonYear == year && r.Status == ClaimRequestStatus.Pending)
            .Select(r => new { Request = r.Email, Account = r.Contributor.Email })
            .ToListAsync(cancellationToken);
        var subscribers = await db.AnnouncementSubscribers.Where(s => s.ConfirmedAt != null).Select(s => s.Email).ToListAsync(cancellationToken);

        var invited = await db.PastAuthorInvitations.Where(i => i.SeasonYear == year).Select(i => i.Fingerprint).ToListAsync(cancellationToken);
        var optedOut = await db.EmailOptOuts.Select(o => o.Fingerprint).ToListAsync(cancellationToken);

        string?[] skipped =
        [
            .. subscribers,
            .. taking.SelectMany(t => new[] { t.Account, t.Contact }),
            .. requesting.SelectMany(r => new[] { r.Request, r.Account }),
        ];
        return Plan(
            spots.Where(s => schedule.IsSeasonOver(s.Year)),
            skipped.OfType<string>().Select(EmailOptOutLinks.Normalize).ToHashSet(StringComparer.Ordinal),
            invited.Concat(optedOut).ToHashSet(StringComparer.Ordinal));
    }

    public async Task<InvitationStatus?> GetStatusAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || await db.Seasons.Where(s => s.Year == year).Select(s => new { s.ClaimsOpenAt }).SingleOrDefaultAsync(cancellationToken) is not { } season)
        {
            return null;
        }

        return new InvitationStatus(
            year,
            season.ClaimsOpenAt,
            (await GetRecipientsAsync(year, cancellationToken)).Count,
            await db.PastAuthorInvitations.CountAsync(i => i.SeasonYear == year, cancellationToken));
    }

    /// <summary>Sends up to <paramref name="budget"/> of the season's invitations. Returns how many went out.</summary>
    public async Task<int> RunAsync(int year, DateTimeOffset now, int budget, CancellationToken cancellationToken = default)
    {
        if (budget <= 0 || year == Season.TestYear)
        {
            return 0;
        }

        var sent = 0;
        foreach (var author in (await GetRecipientsAsync(year, cancellationToken)).Take(budget))
        {
            if (await InviteAsync(year, author, now, cancellationToken))
            {
                sent++;
                await Task.Delay(reminders.Value.PauseBetweenEmails, timeProvider, cancellationToken);
            }
        }

        return sent;
    }

    /// <summary>Invitations sent since <paramref name="since"/>, for the shared hourly allowance.</summary>
    public Task<int> CountSentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        db.PastAuthorInvitations.CountAsync(i => i.SentAt > since, cancellationToken);

    /// <summary>
    /// Who to invite from the finished seasons' spots: one per address (lowercased), named from their profile or else the
    /// name on their latest post, with how many posts they've written and since when. Addresses in
    /// <paramref name="skipAddresses"/>, or whose fingerprint is in <paramref name="skipFingerprints"/>, are left out.
    /// </summary>
    public static IReadOnlyList<PastAuthor> Plan(IEnumerable<PastSpot> spots, IReadOnlySet<string> skipAddresses, IReadOnlySet<string> skipFingerprints)
    {
        var byAddress = new Dictionary<string, List<PastSpot>>(StringComparer.Ordinal);
        foreach (var spot in spots)
        {
            foreach (var address in new[] { spot.ContributorEmail, spot.ContactEmail }.OfType<string>().Select(EmailOptOutLinks.Normalize).Distinct())
            {
                if (!byAddress.TryGetValue(address, out var list))
                {
                    byAddress[address] = list = [];
                }

                list.Add(spot);
            }
        }

        return [.. byAddress
            .Where(entry => !skipAddresses.Contains(entry.Key) && !skipFingerprints.Contains(EmailOptOutLinks.Fingerprint(entry.Key)))
            .Select(entry => new PastAuthor(
                entry.Key,
                entry.Value.Select(s => s.ContributorName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? entry.Value.OrderByDescending(s => s.Year).First().AuthorName,
                entry.Value.Where(s => s.HasPost).Select(s => s.SlotId).Distinct().Count(),
                entry.Value.Min(s => s.Year)))
            .OrderBy(author => author.Address, StringComparer.Ordinal)];
    }

    private async Task<bool> InviteAsync(int year, PastAuthor author, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Recorded first: the unique index means only one run or replica sends each invitation.
        var invitation = new PastAuthorInvitation { SeasonYear = year, Fingerprint = EmailOptOutLinks.Fingerprint(author.Address), SentAt = now };
        db.PastAuthorInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(invitation).State = EntityState.Detached;
            return false;
        }

        try
        {
            var email = PastAuthorEmails.Invitation(author, year, site.Value);
            await sender.SendAsync(await composer.ComposeAsync(email, new EmailRecipient(author.Address, author.Name)), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the address: the logs never carry personal details. A later run tries again.
            LogInvitationFailed(exception, year);
            await db.PastAuthorInvitations.Where(i => i.Id == invitation.Id).ExecuteDeleteAsync(cancellationToken);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send a past author's invitation to C# Advent {Year}; a later run will try again")]
    private partial void LogInvitationFailed(Exception exception, int year);
}
