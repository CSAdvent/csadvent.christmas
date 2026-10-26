using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Jobs;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Backups;

public sealed record BackupRun(int Asked, int Used, int Withdrawn, int Exhausted)
{
    public static readonly BackupRun None = new(0, 0, 0, 0);

    public static BackupRun operator +(BackupRun a, BackupRun b) =>
        new(a.Asked + b.Asked, a.Used + b.Used, a.Withdrawn + b.Withdrawn, a.Exhausted + b.Exhausted);
}

public enum BackupOfferOutcome
{
    /// <summary>On deck: their post goes on the calendar at the reveal unless the original author's link arrives first.</summary>
    StandingBy,

    /// <summary>Their post is on the calendar.</summary>
    Used,

    Declined,

    /// <summary>The spot doesn't need a backup any more (its post arrived, it was filled, or its day is over).</summary>
    NoLongerNeeded,

    TakenByOthers,
    NotFound,

    /// <summary>Filling in for a missing blurb, but their own post doesn't open with it either.</summary>
    MissingBlurb,

    /// <summary>The offer kept changing while they answered (a run closing it, say), so nothing was saved: the page shows where it stands.</summary>
    Changed,
}

/// <summary>A backup's view of one ask, for the offer page.</summary>
public sealed record BackupOfferView(
    int OfferId,
    BackupSpot Spot,
    BackupReason? Reason,
    BackupOfferStatus Status,
    bool TakenByOthers,
    bool IsDayOver,
    BackupAcceptForm Values)
{
    /// <summary>For a missing blurb: when the original author's time runs out, and a backup standing by takes the spot.</summary>
    public DateTimeOffset? Deadline { get; init; }
}

/// <summary>
/// Backup posters filling spots, asked one at a time in sign-up order (see the decisions on #36). A spot needs a backup when
/// <list type="bullet">
/// <item>its author dropped out (or an organizer released it), from then until its day is over: a backup who accepts takes
/// it straight away</item>
/// <item>it still has no post link at <see cref="BackupOptions.AskFrom"/> on the evening before its day: a backup who accepts
/// takes it at the reveal (midnight), unless the original author's link arrives first</item>
/// <item>on its day, its post doesn't open with the C# Advent blurb (#128): a backup whose own post has it takes the spot at
/// the deadline (its announcement), unless the original author adds the blurb first or the organizers keep them, and never
/// sooner than <see cref="BackupOptions.OrganizersNotice"/> after the organizers heard the backup is standing by (#148); or
/// straight away if an organizer asked for a backup</item>
/// </list>
/// Each backup has <see cref="BackupOptions.AnswerWithin"/> before the next is asked too; anyone asked can accept while the
/// spot still needs a post. When everybody's been asked, the organizers hear about it once. That's per round
/// (<see cref="BackupRound"/>, #153): when the spot needs a backup for a new reason or a new author, everyone's asked again.
/// <para>
/// Asks go through the email queue (#152), which drops one not sent by the time the next backup is asked. If an ask can't be
/// queued, the offer goes and the same backup is asked by the next run; if the queue gives up on it (#149), its time to answer
/// ends there and the next backup is asked straight away (<see cref="AskUndeliveredAsync"/>).
/// </para>
/// </summary>
public sealed partial class BackupService(
    AdventDbContext db,
    AdventSchedule schedule,
    EmailComposer composer,
    IEmailSender sender,
    BlurbChecker blurbs,
    IOptions<BackupOptions> options,
    IOptions<SocialPostingOptions> posting,
    IOptions<SiteOptions> site,
    ILogger<BackupService> logger)
{
    /// <summary>When asking starts for a spot whose post link is missing: the evening before its day.</summary>
    public DateTimeOffset GetAskStart(int year, int day) =>
        schedule.FromLocalTime(schedule.GetDate(year, day).AddDays(-1).ToDateTime(options.Value.AskFrom));

    /// <summary>
    /// Whether a backup could still fill the slot, and its day isn't over: it was dropped, it has no post link, or its post's
    /// blurb is missing. Anyone who was asked can accept while this holds.
    /// </summary>
    /// <param name="blurb">The slot's blurb check, if it's about the slot's current post.</param>
    public BackupReason? CouldUseBackup(Slot slot, DateTimeOffset now, BlurbCheck? blurb = null) =>
        now >= schedule.GetDayEndTime(slot.SeasonYear, slot.Day) ? null
        : slot.AuthorName is null ? (slot.DroppedOutAt is not null ? BackupReason.DroppedOut : null)
        : slot.PostUrl is null ? BackupReason.LinkMissing
        : blurb is { Status: BlurbStatus.Missing } && blurb.PostUrl == slot.PostUrl && BlurbRules.AppliesTo(slot.SeasonYear)
            ? BackupReason.BlurbMissing
        : null;

    /// <summary>
    /// Whether it's time to ask backups about the slot: as soon as it's dropped, from the evening before if its link is missing,
    /// and from the reveal if its blurb is (or straight away once an organizer asks for a backup).
    /// </summary>
    public BackupReason? NeedsBackup(Slot slot, DateTimeOffset now, BlurbCheck? blurb = null) => CouldUseBackup(slot, now, blurb) switch
    {
        BackupReason.LinkMissing when now < GetAskStart(slot.SeasonYear, slot.Day) => null,
        BackupReason.BlurbMissing when now < schedule.GetRevealTime(slot) && blurb?.BackupRequestedAt is null => null,
        var reason => reason,
    };

    /// <summary>For a missing blurb: when the original author's time runs out, the post's announcement.</summary>
    public DateTimeOffset GetBlurbDeadline(Slot slot) => BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part);

    /// <summary>
    /// Whether a backup who accepted takes the spot now: a dropped spot straight away, a missing link at the reveal, and a missing
    /// blurb straight away once an organizer asked for a backup, or else at its deadline once the organizers have had their say
    /// (#148): one decided it, they were told at the deadline that nobody had taken it, or they heard a backup is standing by at
    /// least <see cref="BackupOptions.OrganizersNotice"/> ago.
    /// </summary>
    /// <param name="organizersTold">When the organizers heard that a backup is standing by, if they have.</param>
    private bool IsDue(BackupReason reason, Slot slot, BlurbCheck? blurb, DateTimeOffset? organizersTold, DateTimeOffset now) => reason switch
    {
        BackupReason.DroppedOut => true,
        BackupReason.BlurbMissing => blurb?.BackupRequestedAt is not null
            || (now >= GetBlurbDeadline(slot)
                && (blurb?.DecidedBy is not null
                    || blurb?.DeadlineEmailedAt is not null
                    || (organizersTold is { } told && now >= told + options.Value.OrganizersNotice))),
        _ => now >= schedule.GetRevealTime(slot.SeasonYear, slot.Day),
    };

    /// <summary>The slot's blurb check, if it's about the slot's current post.</summary>
    private Task<BlurbCheck?> BlurbOfAsync(Slot slot, CancellationToken cancellationToken) =>
        slot.PostUrl is null
            ? Task.FromResult<BlurbCheck?>(null)
            : db.BlurbChecks.AsNoTracking().SingleOrDefaultAsync(c => c.SlotId == slot.Id && c.PostUrl == slot.PostUrl, cancellationToken);

    /// <summary>For a missing blurb: when the organizers heard that a backup is standing by (<see cref="BlurbCheckService"/>).</summary>
    private async Task<DateTimeOffset?> OrganizersToldAsync(Slot slot, BackupReason reason, CancellationToken cancellationToken) =>
        reason is not BackupReason.BlurbMissing
            ? null
            : await db.SentReminders
                .Where(r => r.SlotId == slot.Id && r.Kind == ReminderKind.AdminBlurbBackupStandingBy)
                .Select(r => (DateTimeOffset?)r.SentAt)
                .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Moves every spot along: closes offers that aren't needed, uses acceptances that are due, and asks the next backups.
    /// <paramref name="onlyYear"/> limits it to one season (the integration tests use it so seasons don't affect each other).
    /// </summary>
    public async Task<BackupRun> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => (onlyYear is null || year == onlyYear) && now < schedule.GetDayEndTime(year, Season.DaysInAdvent))
            .ToList();

        var run = BackupRun.None;
        foreach (var year in years)
        {
            // Spots that might need a backup, and spots with offers that might need closing.
            var slotIds = await db.Slots
                .Where(s => s.SeasonYear == year
                    && ((s.AuthorName == null && s.DroppedOutAt != null)
                        || (s.AuthorName != null && s.PostUrl == null)
                        || (s.AuthorName != null && db.BlurbChecks.Any(c => c.SlotId == s.Id && c.PostUrl == s.PostUrl && c.Status == BlurbStatus.Missing))))
                .Select(s => s.Id)
                .Union(db.BackupOffers
                    .Where(o => o.Slot.SeasonYear == year && (o.Status == BackupOfferStatus.Asked || o.Status == BackupOfferStatus.Accepted))
                    .Select(o => o.SlotId))
                .ToListAsync(cancellationToken);

            // One spot that fails doesn't stop the others' asks (#147).
            await EachSpot.RunAsync(db, slotIds.Order(), async slotId => run += await AdvanceAsync(slotId, now, cancellationToken), LogSpotFailed, cancellationToken);
        }

        return run;
    }

    /// <summary>
    /// Starts on a spot straight away (after a dropout, a release or a blurb decision), instead of waiting for the next run. If
    /// it fails, it's logged and the next run does it (#147), so it never fails what the author or organizer was doing.
    /// </summary>
    public async Task<BackupRun> OfferNowAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        try
        {
            return await AdvanceAsync(slotId, now, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Nothing it left half done may be saved by the caller.
            db.ChangeTracker.Clear();
            if (MissingSchema.IsMissingTableOrColumn(exception))
            {
                LogOfferNowNotMigrated(exception, slotId);
            }
            else
            {
                LogSpotFailed(exception, slotId);
            }

            return BackupRun.None;
        }
    }

    /// <summary>What the email queue knows an offer's ask by.</summary>
    public static string OfferReference(int offerId) => string.Create(CultureInfo.InvariantCulture, $"backup-offer:{offerId}");

    public static bool TryReadOfferReference(string? reference, out int offerId)
    {
        offerId = 0;
        return reference is not null && reference.StartsWith("backup-offer:", StringComparison.Ordinal)
            && int.TryParse(reference.AsSpan("backup-offer:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out offerId);
    }

    /// <summary>
    /// The email queue gave up on an ask (it failed for good, or expired unsent) (#149). The backup never heard, so their
    /// time to answer ends now and the next backup is asked straight away, instead of the spot waiting out their hours.
    /// </summary>
    public async Task<BackupRun> AskUndeliveredAsync(int offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var slotId = await db.BackupOffers.Where(o => o.Id == offerId).Select(o => (int?)o.SlotId).SingleOrDefaultAsync(cancellationToken);
        var ended = await db.BackupOffers
            .Where(o => o.Id == offerId && o.Status == BackupOfferStatus.Asked && o.ExpiresAt > now)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.ExpiresAt, now), cancellationToken);
        if (slotId is null || ended == 0)
        {
            return BackupRun.None;
        }

        LogAskUndelivered(offerId, slotId.Value);

        // Anything tracked from before the update would still have the old time.
        db.ChangeTracker.Clear();
        return await AdvanceAsync(slotId.Value, now, cancellationToken);
    }

    public async Task<BackupOfferView?> GetOfferAsync(int contributorId, int offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var offer = await FindOfferAsync(contributorId, offerId, cancellationToken);
        if (offer is null)
        {
            return null;
        }

        // Someone's standing by, or took the spot after this ask. A backup who took it before this ask (and then dropped out
        // too, say) is history: the spot needs someone again (#153).
        var takenByOthers = await db.BackupOffers.AnyAsync(
            o => o.SlotId == offer.SlotId && o.Id != offer.Id
                && (o.Status == BackupOfferStatus.Accepted || (o.Status == BackupOfferStatus.Used && o.Id > offer.Id)),
            cancellationToken);
        var reason = CouldUseBackup(offer.Slot, now, await BlurbOfAsync(offer.Slot, cancellationToken));

        return new BackupOfferView(
            offer.Id,
            SpotOf(offer.Slot),
            reason,
            offer.Status,
            takenByOthers,
            now >= schedule.GetDayEndTime(offer.Slot.SeasonYear, offer.Slot.Day),
            new BackupAcceptForm
            {
                AuthorName = offer.AuthorName ?? offer.Substitute.Name,
                AuthorUrl = offer.AuthorUrl ?? offer.Substitute.Link,
                PostTitle = offer.PostTitle,
                PostUrl = offer.PostUrl,
            })
        {
            Deadline = reason is BackupReason.BlurbMissing ? GetBlurbDeadline(offer.Slot) : null,
        };
    }

    /// <summary>A backup taking a spot with their post. Anyone who was asked can, while the spot still needs one.</summary>
    public async Task<BackupOfferOutcome> AcceptAsync(int contributorId, int offerId, BackupAcceptForm form, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await TryAcceptAsync(contributorId, offerId, form, now, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                // A run changed the offer at the same moment (closing it, or starting a new round), or the form was sent
                // twice (#147). Nothing was saved, so it's tried again with the offer as it is now.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                return BackupOfferOutcome.Changed;
            }
        }
    }

    private async Task<BackupOfferOutcome> TryAcceptAsync(int contributorId, int offerId, BackupAcceptForm form, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var offer = await FindOfferAsync(contributorId, offerId, cancellationToken);
        if (offer is null)
        {
            return BackupOfferOutcome.NotFound;
        }

        if (offer.Status is BackupOfferStatus.Used)
        {
            return BackupOfferOutcome.Used;
        }

        if (offer.Status is BackupOfferStatus.Accepted)
        {
            return BackupOfferOutcome.StandingBy;
        }

        var blurb = await BlurbOfAsync(offer.Slot, cancellationToken);
        if (CouldUseBackup(offer.Slot, now, blurb) is not { } reason)
        {
            return BackupOfferOutcome.NoLongerNeeded;
        }

        // Filling in for a missing blurb on the day, their post has to be out already and have the blurb itself. Anything the
        // models can't settle is accepted, and the post is checked again once it's on the calendar.
        if (reason is BackupReason.BlurbMissing && Uri.TryCreate(form.PostUrl?.Trim(), UriKind.Absolute, out var postUrl)
            && (await blurbs.CheckAsync(offer.Slot.SeasonYear, postUrl, form.PostTitle, unchanged: null, cancellationToken)).Outcome.Status is BlurbStatus.Missing)
        {
            return BackupOfferOutcome.MissingBlurb;
        }

        offer.Status = BackupOfferStatus.Accepted;
        offer.RespondedAt = now;
        offer.AuthorName = form.AuthorName.Trim();
        offer.AuthorUrl = Clean(form.AuthorUrl);
        offer.PostTitle = form.PostTitle!.Trim();
        offer.PostUrl = form.PostUrl!.Trim();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another backup accepted first (the one-taker index).
            return BackupOfferOutcome.TakenByOthers;
        }

        if (IsDue(reason, offer.Slot, blurb, await OrganizersToldAsync(offer.Slot, reason, cancellationToken), now))
        {
            var others = await db.BackupOffers
                .Where(o => o.SlotId == offer.SlotId && o.Id != offer.Id && o.Status == BackupOfferStatus.Asked)
                .ToListAsync(cancellationToken);
            return (await UseAsync(offer.Slot, offer, others, reason, cancellationToken)).Used > 0
                ? BackupOfferOutcome.Used
                : BackupOfferOutcome.StandingBy;
        }

        return BackupOfferOutcome.StandingBy;
    }

    /// <summary>A backup who can't this time. They stay on the list, and the next run asks the next backup.</summary>
    public async Task<BackupOfferOutcome> DeclineAsync(int contributorId, int offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var offer = await FindOfferAsync(contributorId, offerId, cancellationToken);
        if (offer is null)
        {
            return BackupOfferOutcome.NotFound;
        }

        if (offer.Status is BackupOfferStatus.Asked)
        {
            offer.Status = BackupOfferStatus.Declined;
            offer.RespondedAt = now;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A run closed it at the same moment, or they accepted it in another tab (#147). The page shows where it stands.
                db.ChangeTracker.Clear();
                return BackupOfferOutcome.Changed;
            }
        }

        return BackupOfferOutcome.Declined;
    }

    private async Task<BackupRun> AdvanceAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var slot = await db.Slots.Include(s => s.Contact).Include(s => s.Season).SingleAsync(s => s.Id == slotId, cancellationToken);
        var offers = await db.BackupOffers
            .Include(o => o.Substitute)
            .Where(o => o.SlotId == slotId)
            .OrderBy(o => o.AskedAt)
            .ToListAsync(cancellationToken);
        var open = offers.Where(o => o.Status is BackupOfferStatus.Asked or BackupOfferStatus.Accepted).ToList();
        var blurb = await BlurbOfAsync(slot, cancellationToken);

        if (CouldUseBackup(slot, now, blurb) is not { } stillNeeded)
        {
            return await CloseAsync(slot, open, now, cancellationToken);
        }

        if (open.FirstOrDefault(o => o.Status is BackupOfferStatus.Accepted) is { } accepted)
        {
            return IsDue(stillNeeded, slot, blurb, await OrganizersToldAsync(slot, stillNeeded, cancellationToken), now)
                ? await UseAsync(slot, accepted, open.Where(o => o != accepted), stillNeeded, cancellationToken)
                : BackupRun.None;
        }

        // A missing link isn't chased until the evening before, or a missing blurb until the reveal.
        if (NeedsBackup(slot, now, blurb) is not { } reason)
        {
            return BackupRun.None;
        }

        // Everyone's asked once per round, and a new author or a new reason starts a new one (#153).
        if (await CurrentRoundAsync(slot, reason, offers, now, cancellationToken) is not { } round)
        {
            return BackupRun.None;
        }

        var asked = offers.Where(o => o.Id > round.OffersAfter).ToList();

        // Someone's still within their time to answer.
        if (asked.Any(o => o.Status is BackupOfferStatus.Asked && o.ExpiresAt > now))
        {
            return BackupRun.None;
        }

        if (await NextCandidateAsync(slot, asked, now, cancellationToken) is not { } candidate)
        {
            return await TellOrganizersNobodyIsLeftAsync(slot, round, now, cancellationToken);
        }

        return await AskAsync(slot, candidate, reason, now, cancellationToken);
    }

    /// <summary>
    /// The spot's round of asks (#153): its latest round, or a new one if the spot now needs a backup for another reason or
    /// another author (<see cref="BackupRound.HolderOf"/>). A new round withdraws the earlier asks nobody answered, so their
    /// time to answer doesn't hold this round up and they can be asked again; a backup standing by stays on deck. Null if
    /// another run (or replica) started it, or answered an ask, at the same moment: the next run carries on.
    /// </summary>
    private async Task<BackupRound?> CurrentRoundAsync(Slot slot, BackupReason reason, List<BackupOffer> offers, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var latest = await db.BackupRounds.Where(r => r.SlotId == slot.Id).OrderByDescending(r => r.Number).FirstOrDefaultAsync(cancellationToken);
        var round = BackupRound.For(latest, slot, reason, offers.Count == 0 ? 0 : offers.Max(o => o.Id), now);
        if (round == latest)
        {
            return latest;
        }

        db.BackupRounds.Add(round);
        foreach (var offer in offers.Where(o => o.Status is BackupOfferStatus.Asked))
        {
            offer.Status = BackupOfferStatus.Withdrawn;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return round;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    /// <summary>The first backup in sign-up order who hasn't been asked about this spot this round and isn't busy with another.</summary>
    private async Task<Substitute?> NextCandidateAsync(Slot slot, List<BackupOffer> offers, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var asked = offers.Select(o => o.SubstituteId).ToHashSet();
        var busy = (await db.BackupOffers
            .Where(o => o.Slot.SeasonYear == slot.SeasonYear
                && o.SlotId != slot.Id
                && ((o.Status == BackupOfferStatus.Asked && o.ExpiresAt > now) || o.Status == BackupOfferStatus.Accepted))
            .Select(o => o.SubstituteId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var substitutes = await db.Substitutes
            .Where(s => s.SeasonYear == slot.SeasonYear && s.AssignedSlotId == null)
            .OrderBy(s => s.SignedUpAt).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        // Never the spot's own author, whose link is the one that's late.
        return substitutes.FirstOrDefault(s =>
            !asked.Contains(s.Id) && !busy.Contains(s.Id) && (slot.ContributorId is null || s.ContributorId != slot.ContributorId));
    }

    private async Task<BackupRun> AskAsync(Slot slot, Substitute candidate, BackupReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var offer = new BackupOffer
        {
            SlotId = slot.Id,
            SubstituteId = candidate.Id,
            AskedAt = now,
            // On the day, a missing blurb's deadline is hours away, so each backup gets less time.
            ExpiresAt = now + (reason is BackupReason.BlurbMissing ? options.Value.AnswerWithinOnTheDay : options.Value.AnswerWithin),
            Status = BackupOfferStatus.Asked,
        };
        db.BackupOffers.Add(offer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another run (or replica) asked them at the same moment.
            db.Entry(offer).State = EntityState.Detached;
            return BackupRun.None;
        }

        var answerBy = schedule.ToLocalTime(offer.ExpiresAt);
        var ask = BackupEmails.Ask(
            candidate.Name,
            SpotOf(slot),
            reason,
            string.Create(CultureInfo.InvariantCulture, $"{answerBy:h:mm tt} Eastern on {answerBy:MMMM d}"),
            $"{Site}/backups/offers/{offer.Id}",
            site.Value,
            reason is BackupReason.BlurbMissing ? Eastern(GetBlurbDeadline(slot)) : null);

        // Once their time's up the next backup is asked too, so an ask still waiting in the queue by then is dropped.
        if (!await SendAsync(ask with { SendBefore = offer.ExpiresAt, Reference = OfferReference(offer.Id) }, new EmailRecipient(candidate.Email, candidate.Name), "backup ask", slot.Id, cancellationToken))
        {
            // They were never asked, through no fault of theirs, so the offer goes and the next run asks them again (#149).
            db.BackupOffers.Remove(offer);
            await db.SaveChangesAsync(CancellationToken.None);
            return BackupRun.None;
        }

        return BackupRun.None with { Asked = 1 };
    }

    /// <summary>Puts the backup's post on the calendar in place of the spot's author (if any), who's never named again.</summary>
    private async Task<BackupRun> UseAsync(Slot slot, BackupOffer offer, IEnumerable<BackupOffer> others, BackupReason reason, CancellationToken cancellationToken)
    {
        var original = reason is BackupReason.LinkMissing or BackupReason.BlurbMissing && slot.AuthorName is { } author && slot.Contact is { } contact
            ? new EmailRecipient(contact.Email, author)
            : null;
        var substitute = offer.Substitute ?? await db.Substitutes.SingleAsync(s => s.Id == offer.SubstituteId, cancellationToken);

        slot.AuthorName = offer.AuthorName;
        slot.AuthorUrl = offer.AuthorUrl;
        slot.PostTitle = offer.PostTitle;
        slot.PostUrl = offer.PostUrl;
        // The summary was the original author's, about their post.
        slot.PostSummary = null;
        slot.ContributorId = substitute.ContributorId;
        slot.DroppedOutAt = null;
        if (slot.Contact is null)
        {
            slot.Contact = new SlotContact { Email = substitute.Email };
        }
        else
        {
            slot.Contact.Email = substitute.Email;
            slot.Contact.DailyEmailsOptedOutAt = null;
        }

        offer.Status = BackupOfferStatus.Used;
        substitute.AssignedSlotId = slot.Id;
        foreach (var other in others.Where(o => o.Status is BackupOfferStatus.Asked))
        {
            other.Status = BackupOfferStatus.Withdrawn;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed the spot at the same moment (xmin). Nothing was saved; the next run tries again.
            db.ChangeTracker.Clear();
            return BackupRun.None;
        }

        var spot = SpotOf(slot);
        await SendAsync(BackupEmails.YoureOn(substitute.Name, spot, offer.PostTitle!, site.Value), new EmailRecipient(substitute.Email, substitute.Name), "backup used", slot.Id, cancellationToken);
        await SendAsync(BackupEmails.SpotFilled(substitute.Name, spot, reason, site.Value), Organizers, "spot filled", slot.Id, cancellationToken);
        if (original is not null)
        {
            await SendAsync(BackupEmails.Replaced(original.Name!, spot, site.Value, reason), original, "author replaced", slot.Id, cancellationToken);
        }

        return BackupRun.None with { Used = 1 };
    }

    /// <summary>Closes the open offers of a spot that doesn't need a backup any more.</summary>
    private async Task<BackupRun> CloseAsync(Slot slot, List<BackupOffer> open, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (open.Count == 0)
        {
            return BackupRun.None;
        }

        foreach (var offer in open)
        {
            offer.Status = BackupOfferStatus.Withdrawn;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A backup answered one at the same moment (xmin, #147). Nothing was saved; the next run closes them.
            db.ChangeTracker.Clear();
            return BackupRun.None;
        }

        // Backups who'd sent a post hear that the spot was filled after all (not when its day simply ended).
        if (now < schedule.GetDayEndTime(slot.SeasonYear, slot.Day))
        {
            foreach (var offer in open.Where(o => o.RespondedAt is not null && o.PostUrl is not null))
            {
                await SendAsync(
                    BackupEmails.NotNeeded(offer.Substitute.Name, SpotOf(slot), site.Value),
                    new EmailRecipient(offer.Substitute.Email, offer.Substitute.Name),
                    "backup not needed",
                    slot.Id,
                    cancellationToken);
            }
        }

        return BackupRun.None with { Withdrawn = open.Count };
    }

    private async Task<BackupRun> TellOrganizersNobodyIsLeftAsync(Slot slot, BackupRound round, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Once per round (#153). Every run gets here until the spot's day is over or a new round starts, so it checks first,
        // and the run (or replica) whose update sets the time sends it: nothing is inserted for an index to refuse (#157).
        if (round.NobodyLeftAt is not null
            || await db.BackupRounds
                .Where(r => r.Id == round.Id && r.NobodyLeftAt == null)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.NobodyLeftAt, now), cancellationToken) == 0)
        {
            return BackupRun.None;
        }

        if (!await SendAsync(BackupEmails.NoBackups(SpotOf(slot), site.Value), Organizers, "no backups left", slot.Id, cancellationToken))
        {
            // Not queued, so not recorded either: the next run tries again (#149).
            await db.BackupRounds
                .Where(r => r.Id == round.Id)
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.NobodyLeftAt, (DateTimeOffset?)null), CancellationToken.None);
            return BackupRun.None;
        }

        return BackupRun.None with { Exhausted = 1 };
    }

    private Task<BackupOffer?> FindOfferAsync(int contributorId, int offerId, CancellationToken cancellationToken) =>
        db.BackupOffers
            .Include(o => o.Substitute)
            .Include(o => o.Slot).ThenInclude(s => s.Contact)
            .Include(o => o.Slot).ThenInclude(s => s.Season)
            .SingleOrDefaultAsync(o => o.Id == offerId && o.Substitute.ContributorId == contributorId, cancellationToken);

    /// <summary>Queues the email. False if it couldn't be queued (the database failed), which is logged.</summary>
    private async Task<bool> SendAsync(EmailContent content, EmailRecipient to, string kind, int slotId, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(await composer.ComposeAsync(content, to), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailed(exception, kind, slotId);
            return false;
        }
    }

    private static BackupSpot SpotOf(Slot slot) => new(slot.SeasonYear, slot.Day, slot.Part, slot.Season.SlotsPerDay);

    /// <summary>A time as emails give it: "10:00 AM Eastern on December 6".</summary>
    private string Eastern(DateTimeOffset moment)
    {
        var local = schedule.ToLocalTime(moment);
        return string.Create(CultureInfo.InvariantCulture, $"{local:h:mm tt} Eastern on {local:MMMM d}");
    }

    private EmailRecipient Organizers => new(site.Value.ContactEmail, "C# Advent organizers");

    private string Site => site.Value.BaseUrl.TrimEnd('/');

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the {Kind} email for slot {SlotId}")]
    private partial void LogSendFailed(Exception exception, string kind, int slotId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup offer {OfferId}'s ask for slot {SlotId} couldn't be sent, so the next backup is being asked")]
    private partial void LogAskUndelivered(int offerId, int slotId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Moving the backup asks for slot {SlotId} along failed. The next run tries it again")]
    private partial void LogSpotFailed(Exception exception, int slotId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't ask backups about slot {SlotId} yet: the database isn't migrated to this version. The next run does")]
    private partial void LogOfferNowNotMigrated(Exception exception, int slotId);
}

/// <summary>
/// When the email queue gives up on a backup ask (#149), its offer ends so the next backup is asked
/// (<see cref="BackupService.AskUndeliveredAsync"/>).
/// </summary>
public sealed class BackupAskUndelivered(BackupService backups) : IUndeliveredEmailHandler
{
    public async Task HandleAsync(UndeliveredEmail email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (BackupService.TryReadOfferReference(email.Reference, out var offerId))
        {
            await backups.AskUndeliveredAsync(offerId, now, cancellationToken);
        }
    }
}

/// <summary>What a backup sends to fill a spot: their post, and how they appear on the calendar.</summary>
public sealed class BackupAcceptForm
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(50, ErrorMessage = "Keep your name to 50 characters or fewer.")]
    public string AuthorName { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the link to 200 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Your website must be a full web address that starts with https:// or http://.")]
    [NotSocialProfile]
    public string? AuthorUrl { get; set; }

    [Required(ErrorMessage = "Enter your post's title.")]
    [StringLength(300, ErrorMessage = "Keep the title to 300 characters or fewer.")]
    public string? PostTitle { get; set; }

    [Required(ErrorMessage = "Enter your post's URL.")]
    [StringLength(500, ErrorMessage = "Keep the URL to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Your post's URL must be a full web address that starts with https:// or http://.")]
    public string? PostUrl { get; set; }
}
