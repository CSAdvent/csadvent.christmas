using System.Globalization;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Blurbs;

/// <param name="AskedClaude">How many of the checks went to Claude (the rest were settled without it).</param>
public sealed record BlurbRun(int Checked, int Emailed, int AskedClaude = 0)
{
    public static readonly BlurbRun None = new(0, 0);

    public static BlurbRun operator +(BlurbRun a, BlurbRun b) => new(a.Checked + b.Checked, a.Emailed + b.Emailed, a.AskedClaude + b.AskedClaude);
}

/// <summary>What an organizer decided about a post's blurb.</summary>
public enum BlurbDecision
{
    /// <summary>The blurb is there, whatever Claude said.</summary>
    Passed,

    /// <summary>It isn't: the author is emailed and a backup is lined up.</summary>
    Missing,

    /// <summary>It isn't, but announce the post anyway, and stop asking backups.</summary>
    AnnounceAnyway,
}

/// <summary>
/// Checks each post for the C# Advent blurb (#128) and acts on the answer. <see cref="Reminders.ReminderWorker"/> runs it every
/// 15 minutes, before the reminders and backups:
/// <list type="bullet">
/// <item>Posts are checked from <see cref="BlurbOptions.CheckFrom"/> before their reveal until their day is over, until they
/// pass. A changed URL starts again, and an unchanged opening isn't sent to Claude twice. Authors can check their own post
/// any time before their day is over (<see cref="CheckForAuthorAsync"/>, #174).</item>
/// <item>A page that doesn't load, or doesn't seem to be the post, counts as not published yet until
/// <see cref="BlurbOptions.PublishedBy"/> before its deadline (#159).</item>
/// <item>From the reveal: a missing blurb emails the author once and lines up a backup (<see cref="BackupService"/>); once a
/// backup is standing by, the organizers hear about it, with time to keep the author (#148); a post Claude can't settle, or
/// that couldn't be checked, emails the organizers; and at the deadline, with nobody to take the spot, they're told again.
/// Every email is claimed in the database before it's queued, so replicas never send one twice, and the claim is undone if it
/// can't be queued, so a later run tries again (#149). Once queued, the email queue sees it through.</item>
/// <item>Organizers can check again, decide, and ask for a backup.</item>
/// </list>
/// </summary>
public sealed partial class BlurbCheckService(
    AdventDbContext db,
    AdventSchedule schedule,
    BlurbChecker checker,
    BackupService backups,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<BlurbOptions> options,
    IOptions<BackupOptions> backupOptions,
    IOptions<SocialPostingOptions> posting,
    IOptions<SiteOptions> site,
    ILogger<BlurbCheckService> logger)
{
    /// <summary>How long a run holds a post it's checking, so another replica leaves it alone.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>A post that couldn't be checked this long tells the organizers, so a passing hiccup doesn't.</summary>
    public static readonly TimeSpan CouldNotCheckGrace = TimeSpan.FromMinutes(30);

    public bool IsEnabled => options.Value.IsEnabled;

    /// <param name="onlyYear">Only this season (the integration tests use it so seasons don't affect each other).</param>
    public async Task<BlurbRun> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return BlurbRun.None;
        }

        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => (onlyYear is null || year == onlyYear)
                && BlurbRules.AppliesTo(year)
                && now >= schedule.GetRevealTime(year, 1) - options.Value.CheckFrom
                && now < schedule.GetDayEndTime(year, Season.DaysInAdvent))
            .ToList();

        var run = BlurbRun.None;
        foreach (var year in years)
        {
            var slots = await db.Slots
                .AsNoTracking()
                .Where(s => s.SeasonYear == year && s.AuthorName != null && s.PostUrl != null)
                .OrderBy(s => s.Day).ThenBy(s => s.Part)
                .Select(s => new { s.Id, s.Day })
                .ToListAsync(cancellationToken);

            foreach (var slot in slots.Where(s => now >= schedule.GetRevealTime(year, s.Day) - options.Value.CheckFrom && now < schedule.GetDayEndTime(year, s.Day)))
            {
                run += await AdvanceAsync(slot.Id, now, organizer: null, cancellationToken);
            }
        }

        return run;
    }

    /// <summary>"Check now": checks the post again straight away, even one that passed or that an organizer decided.</summary>
    public async Task<BlurbStatus?> CheckNowAsync(int slotId, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await AdvanceAsync(slotId, now, organizer, cancellationToken);
        return await db.BlurbChecks.AsNoTracking().Where(c => c.SlotId == slotId).Select(c => (BlurbStatus?)c.Status).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>An organizer's decision, which stands until the post changes or someone checks it again.</summary>
    public async Task<bool> DecideAsync(int slotId, BlurbDecision decision, string organizer, DateTimeOffset now, bool requestBackup = false, CancellationToken cancellationToken = default)
    {
        var slot = await LoadSlotAsync(slotId, cancellationToken);
        if (slot?.PostUrl is null || !BlurbRules.AppliesTo(slot.SeasonYear))
        {
            return false;
        }

        var check = await EnsureAsync(slot, now, cancellationToken);
        var status = decision switch
        {
            BlurbDecision.Passed => BlurbStatus.Passed,
            BlurbDecision.Missing => BlurbStatus.Missing,
            _ => BlurbStatus.Waived,
        };

        SetStatus(check, status, decision switch
        {
            BlurbDecision.Passed => $"{organizer} marked it as passed.",
            BlurbDecision.Missing => requestBackup ? $"{organizer} asked for a backup." : $"{organizer} marked the blurb as missing.",
            _ => $"{organizer} let it be announced without the blurb.",
        }, now);
        check.DecidedBy = organizer;
        check.DecidedAt = now;
        check.BackupRequestedAt = requestBackup ? now : null;
        await db.SaveChangesAsync(cancellationToken);

        // They've decided, so any word about a backup standing by is history.
        await ForgetBackupNoticeAsync(slot.Id, cancellationToken);
        await NotifyAsync(check, slot, now, cancellationToken);

        // Lines up (or uses) a backup for a missing blurb, and closes the offers of one that passed or was let through.
        await backups.OfferNowAsync(slot.Id, now, cancellationToken);
        return true;
    }

    /// <summary>
    /// "Use a backup", for an author who won't add the blurb: marks it missing, and the next backup who accepts takes the spot
    /// straight away (or one already standing by does now).
    /// </summary>
    public Task<bool> UseBackupAsync(int slotId, string organizer, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        DecideAsync(slotId, BlurbDecision.Missing, organizer, now, requestBackup: true, cancellationToken);

    /// <summary>
    /// "Check my post" (#174): the author's own check of their post, made just as a run would make it, but whenever they ask, even
    /// days before their post is first looked at. Before the reveal it's only information: nobody's emailed and no backup is
    /// asked. A post that passed, or that an organizer decided, isn't checked again. Who may ask, and how often, is
    /// <see cref="BlurbSelfCheckService"/>'s business.
    /// </summary>
    public Task<BlurbRun> CheckForAuthorAsync(int slotId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        IsEnabled ? AdvanceAsync(slotId, now, organizer: null, cancellationToken) : Task.FromResult(BlurbRun.None);

    /// <summary>Checks one post, saves what was found, and acts on it. With an organizer, it's their "Check now".</summary>
    private async Task<BlurbRun> AdvanceAsync(int slotId, DateTimeOffset now, string? organizer, CancellationToken cancellationToken)
    {
        var slot = await LoadSlotAsync(slotId, cancellationToken);
        if (slot?.PostUrl is null || slot.AuthorName is null || !BlurbRules.AppliesTo(slot.SeasonYear))
        {
            return BlurbRun.None;
        }

        var check = await EnsureAsync(slot, now, cancellationToken);
        var run = BlurbRun.None;
        var settled = check.DecidedBy is not null || check.Status is BlurbStatus.Passed or BlurbStatus.Waived;
        if (organizer is not null || !settled)
        {
            if (!await LeaseAsync(check.Id, now, cancellationToken))
            {
                return BlurbRun.None;
            }

            // A post Claude already answered about isn't sent to it again while its opening is the same.
            var unchanged = organizer is null && check.Status is BlurbStatus.Missing or BlurbStatus.NeedsDecision ? check.ContentHash : null;
            var result = await checker.CheckAsync(slot.SeasonYear, new Uri(slot.PostUrl), slot.PostTitle, unchanged, cancellationToken);
            var before = check.Status;
            Record(check, slot, result, now);
            if (organizer is not null)
            {
                check.DecidedBy = null;
                check.DecidedAt = null;
                check.BackupRequestedAt = null;
            }

            await db.SaveChangesAsync(cancellationToken);
            run += new BlurbRun(1, 0, result.Answer is null ? 0 : 1);

            // Once it passes, a later failure is a new one, and a backup standing by for it is worth telling the organizers about.
            if (check.Status is BlurbStatus.Passed && before is not BlurbStatus.Passed)
            {
                await ForgetBackupNoticeAsync(slot.Id, cancellationToken);
            }
        }

        return run + await NotifyAsync(check, slot, now, cancellationToken) + await DeadlineAsync(check, slot, now, cancellationToken);
    }

    /// <summary>Saves a check's result: the post's status, why, and Claude's answer.</summary>
    private void Record(BlurbCheck check, Slot slot, BlurbCheckResult result, DateTimeOffset now)
    {
        check.CheckedAt = now;
        check.LeasedUntil = null;
        db.Entry(check).Property(c => c.LeasedUntil).IsModified = true;
        if (result.Unchanged)
        {
            return;
        }

        var outcome = result.Outcome;

        // A page that doesn't load, or doesn't seem to be the post, is most likely not published yet: many authors publish on
        // the morning of their day (#159). It waits until a few hours before its deadline, and only then goes to the organizers.
        if (result.MaybeUnpublished && now < PublishBy(slot))
        {
            outcome = outcome with { Status = BlurbStatus.Waiting };
        }

        SetStatus(check, outcome.Status, outcome.Detail, now);
        check.ContentHash = result.Opening?.Hash ?? check.ContentHash;
        check.Quote = result.Answer?.Quote;
        check.Reason = result.Answer?.Reason;
    }

    /// <summary>
    /// From the reveal: emails the author once about a missing blurb (and lines up a backup), and the organizers about a post
    /// that needs a decision or couldn't be checked for a while.
    /// </summary>
    private async Task<BlurbRun> NotifyAsync(BlurbCheck check, Slot slot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if ((now < schedule.GetRevealTime(slot) && check.BackupRequestedAt is null) || now >= schedule.GetDayEndTime(slot.SeasonYear, slot.Day))
        {
            return BlurbRun.None;
        }

        var emailed = 0;
        if (check.Status is BlurbStatus.Missing)
        {
            // Asked to add it, unless an organizer is already handing the spot to a backup.
            if (slot.Contact is { } contact
                && check.BackupRequestedAt is null
                && await ClaimAsync(check.Id, c => c.AuthorEmailedAt == null && c.Status == BlurbStatus.Missing, c => c.SetProperty(x => x.AuthorEmailedAt, now), cancellationToken))
            {
                if (await SendAsync(BlurbEmails.Missing(slot.AuthorName!, PostOf(slot), check.Detail ?? "", site.Value), new EmailRecipient(contact.Email, slot.AuthorName), slot.Id, cancellationToken))
                {
                    emailed++;
                }
                else
                {
                    // A later run tries again.
                    await db.BlurbChecks.Where(c => c.Id == check.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.AuthorEmailedAt, (DateTimeOffset?)null), CancellationToken.None);
                }
            }

            // Asks the first backup now, rather than at the next backup run.
            await backups.OfferNowAsync(slot.Id, now, cancellationToken);
            emailed += await TellOrganizersBackupStandingByAsync(check, slot, now, cancellationToken);
        }
        else if ((check.Status is BlurbStatus.NeedsDecision || (check.Status is BlurbStatus.CouldNotCheck && now - check.StatusSince >= CouldNotCheckGrace))
            && check.OrganizersEmailedAbout != check.Status)
        {
            var status = check.Status;
            var before = check.OrganizersEmailedAbout;
            if (await ClaimAsync(check.Id, c => c.Status == status && (c.OrganizersEmailedAbout == null || c.OrganizersEmailedAbout != status), c => c.SetProperty(x => x.OrganizersEmailedAbout, status), cancellationToken))
            {
                if (await SendAsync(BlurbEmails.NeedsDecision(PostOf(slot), status, check.Detail ?? "", site.Value), Organizers, slot.Id, cancellationToken))
                {
                    emailed++;
                }
                else
                {
                    // A later run tries again (#149).
                    await db.BlurbChecks
                        .Where(c => c.Id == check.Id && c.OrganizersEmailedAbout == status)
                        .ExecuteUpdateAsync(c => c.SetProperty(x => x.OrganizersEmailedAbout, before), CancellationToken.None);
                }
            }
        }

        return new BlurbRun(0, emailed);
    }

    /// <summary>
    /// Before a backup takes a spot whose post the check found without the blurb, the organizers hear that one is standing by,
    /// once, so they can keep the author if the check got it wrong (#148). The backup takes the spot no sooner than
    /// <see cref="BackupOptions.OrganizersNotice"/> after this (<see cref="BackupService"/>). Not when an organizer decided it
    /// themselves, or after the deadline's email, which already says a backup who accepts takes the spot straight away.
    /// </summary>
    private async Task<int> TellOrganizersBackupStandingByAsync(BlurbCheck check, Slot slot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (check.DecidedBy is not null || check.DeadlineEmailedAt is not null)
        {
            return 0;
        }

        var backup = await db.BackupOffers
            .AsNoTracking()
            .Where(o => o.SlotId == slot.Id && o.Status == BackupOfferStatus.Accepted)
            .Select(o => new { o.AuthorName, o.PostTitle, o.PostUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (backup?.PostUrl is null)
        {
            return 0;
        }

        // Claimed once per spot; its time is when they were told, which the backup waits on.
        var record = new SentReminder
        {
            Kind = ReminderKind.AdminBlurbBackupStandingBy,
            RunDate = schedule.GetDate(slot.SeasonYear, slot.Day),
            SlotId = slot.Id,
            SentAt = now,
        };
        db.SentReminders.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(record).State = EntityState.Detached;
            return 0;
        }

        var deadline = BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part);
        var takesOver = Eastern(deadline > now + backupOptions.Value.OrganizersNotice ? deadline : now + backupOptions.Value.OrganizersNotice);
        var email = BlurbEmails.BackupStandingBy(PostOf(slot), new BlurbBackup(backup.AuthorName ?? "A backup", backup.PostTitle, backup.PostUrl), check.Detail ?? "", takesOver, site.Value);
        if (await SendAsync(email, Organizers, slot.Id, cancellationToken))
        {
            return 1;
        }

        // Not queued, so not recorded either: a later run tries again (#149), and the backup waits for it.
        db.SentReminders.Remove(record);
        await db.SaveChangesAsync(CancellationToken.None);
        return 0;
    }

    /// <summary>Forgets that the organizers heard about a backup standing by, once that's history: the post changed or passed, or they decided.</summary>
    private Task ForgetBackupNoticeAsync(int slotId, CancellationToken cancellationToken) =>
        db.SentReminders.Where(r => r.SlotId == slotId && r.Kind == ReminderKind.AdminBlurbBackupStandingBy).ExecuteDeleteAsync(cancellationToken);

    /// <summary>At the deadline, a missing blurb with nobody to take the spot tells the organizers, once.</summary>
    private async Task<BlurbRun> DeadlineAsync(BlurbCheck check, Slot slot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (check.Status is not BlurbStatus.Missing || check.DeadlineEmailedAt is not null
            || now < BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part)
            || now >= schedule.GetDayEndTime(slot.SeasonYear, slot.Day)
            || await db.BackupOffers.AnyAsync(o => o.SlotId == slot.Id && (o.Status == BackupOfferStatus.Accepted || o.Status == BackupOfferStatus.Used), cancellationToken))
        {
            return BlurbRun.None;
        }

        if (!await ClaimAsync(check.Id, c => c.DeadlineEmailedAt == null, c => c.SetProperty(x => x.DeadlineEmailedAt, now), cancellationToken))
        {
            return BlurbRun.None;
        }

        if (await SendAsync(BlurbEmails.DeadlinePassed(PostOf(slot), site.Value), Organizers, slot.Id, cancellationToken))
        {
            return new BlurbRun(0, 1);
        }

        // A later run tries again (#149): this is the email that says the post won't be announced until someone decides.
        await db.BlurbChecks.Where(c => c.Id == check.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.DeadlineEmailedAt, (DateTimeOffset?)null), CancellationToken.None);
        return BlurbRun.None;
    }

    /// <summary>The slot's check, created when it's first needed, and started again when the post's URL has changed.</summary>
    private async Task<BlurbCheck> EnsureAsync(Slot slot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var check = await db.BlurbChecks.SingleOrDefaultAsync(c => c.SlotId == slot.Id, cancellationToken);
        if (check is null)
        {
            check = new BlurbCheck { SlotId = slot.Id, PostUrl = slot.PostUrl!, Status = BlurbStatus.Waiting, StatusSince = now };
            db.BlurbChecks.Add(check);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another run created it at the same moment.
                db.Entry(check).State = EntityState.Detached;
                check = await db.BlurbChecks.SingleAsync(c => c.SlotId == slot.Id, cancellationToken);
            }
        }
        else if (check.PostUrl != slot.PostUrl)
        {
            // A new post (an edit, or a backup's): everything about the old one is history.
            check.PostUrl = slot.PostUrl!;
            check.Status = BlurbStatus.Waiting;
            check.StatusSince = now;
            check.Detail = null;
            check.Quote = null;
            check.Reason = null;
            check.ContentHash = null;
            check.DecidedBy = null;
            check.DecidedAt = null;
            check.BackupRequestedAt = null;
            check.AuthorEmailedAt = null;
            check.OrganizersEmailedAbout = null;
            check.DeadlineEmailedAt = null;
            await db.SaveChangesAsync(cancellationToken);
            await ForgetBackupNoticeAsync(slot.Id, cancellationToken);
        }

        return check;
    }

    private static void SetStatus(BlurbCheck check, BlurbStatus status, string detail, DateTimeOffset now)
    {
        if (check.Status != status)
        {
            check.StatusSince = now;
        }

        check.Status = status;
        check.Detail = detail[..Math.Min(detail.Length, 500)];

        // Once it passes (or is let through), a later failure is a new one, worth an email of its own.
        if (status is BlurbStatus.Passed or BlurbStatus.Waived)
        {
            check.AuthorEmailedAt = null;
            check.DeadlineEmailedAt = null;
        }
    }

    private async Task<bool> LeaseAsync(int checkId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.BlurbChecks
            .Where(c => c.Id == checkId && (c.LeasedUntil == null || c.LeasedUntil < now))
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.LeasedUntil, now + Lease), cancellationToken) == 1;

    /// <summary>Records an email as sent, only if it hasn't been: the run (or replica) that changes the row sends it.</summary>
    private async Task<bool> ClaimAsync(
        int checkId,
        System.Linq.Expressions.Expression<Func<BlurbCheck, bool>> unclaimed,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<BlurbCheck>> claim,
        CancellationToken cancellationToken)
    {
        var claimed = await db.BlurbChecks.Where(c => c.Id == checkId).Where(unclaimed).ExecuteUpdateAsync(claim, cancellationToken) == 1;
        if (claimed)
        {
            // The tracked copy would otherwise overwrite the claim on the next save.
            await db.Entry(await db.BlurbChecks.SingleAsync(c => c.Id == checkId, cancellationToken)).ReloadAsync(cancellationToken);
        }

        return claimed;
    }

    private async Task<bool> SendAsync(EmailContent content, EmailRecipient to, int slotId, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(await composer.ComposeAsync(content, to), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailed(exception, content.Subject, slotId);
            return false;
        }
    }

    private Task<Slot?> LoadSlotAsync(int slotId, CancellationToken cancellationToken) =>
        db.Slots.AsNoTracking().Include(s => s.Contact).Include(s => s.Season).SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);

    private BlurbPost PostOf(Slot slot) => new(
        slot.Id,
        new BackupSpot(slot.SeasonYear, slot.Day, slot.Part, slot.Season.SlotsPerDay),
        slot.PostTitle,
        slot.PostUrl!,
        Eastern(BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part)));

    /// <summary>Until when a post whose page doesn't load, or doesn't seem to be the post, counts as not published yet.</summary>
    private DateTimeOffset PublishBy(Slot slot) =>
        BlurbRules.Deadline(schedule, posting.Value, slot.SeasonYear, slot.Day, slot.Part) - options.Value.PublishedBy;

    /// <summary>A time as emails give it: "10:00 AM Eastern on December 6".</summary>
    private string Eastern(DateTimeOffset moment)
    {
        var local = schedule.ToLocalTime(moment);
        return string.Create(CultureInfo.InvariantCulture, $"{local:h:mm tt} Eastern on {local:MMMM d}");
    }

    private EmailRecipient Organizers => new(site.Value.ContactEmail, "C# Advent organizers");

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue \"{Subject}\" for slot {SlotId}")]
    private partial void LogSendFailed(Exception exception, string subject, int slotId);
}
