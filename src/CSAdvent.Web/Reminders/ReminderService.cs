using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Reminders;

public sealed record ReminderRun(int PostLinkReminders, int DailyShares)
{
    public static readonly ReminderRun None = new(0, 0);
}

/// <summary>
/// Sends the day's reminder emails once the reveal time zone reaches <see cref="ReminderOptions.SendAt"/>:
/// <list type="bullet">
/// <item>authors without a post link, <see cref="ReminderOptions.DaysBefore"/> days before their day</item>
/// <item>every author in the season, each day of Advent, the day's revealed posts to share (unless they opted out)</item>
/// </list>
/// Each email is recorded in <c>sent_reminders</c> before it's sent, and the unique index there means only one run (and
/// one replica) can send it. Runs are cheap when there's nothing to do, so the worker calls this often.
/// </summary>
public sealed partial class ReminderService(
    AdventDbContext db,
    AdventSchedule schedule,
    EmailComposer composer,
    IEmailSender sender,
    DailyEmailOptOut optOut,
    IOptions<ReminderOptions> options,
    IOptions<SiteOptions> site,
    ILogger<ReminderService> logger)
{
    public async Task<ReminderRun> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var local = schedule.ToLocalTime(now).DateTime;
        if (TimeOnly.FromDateTime(local) < options.Value.SendAt)
        {
            return ReminderRun.None;
        }

        // Seasons whose reminders or Advent days include today. There's one season per year, so this is a short list.
        var today = DateOnly.FromDateTime(local);
        var earliestReminder = options.Value.DaysBefore.Max();
        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => today >= FirstDay(year).AddDays(-earliestReminder) && today <= LastDay(year))
            .ToList();

        var run = ReminderRun.None;
        foreach (var year in years)
        {
            run = new ReminderRun(
                run.PostLinkReminders + await SendPostLinkRemindersAsync(year, today, now, cancellationToken),
                run.DailyShares + await SendDailySharesAsync(year, today, now, cancellationToken));
        }

        return run;
    }

    private async Task<int> SendPostLinkRemindersAsync(int year, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The Advent days that are exactly one of the reminder intervals away.
        var days = options.Value.DaysBefore
            .Select(daysBefore => schedule.GetDayOfAdvent(year, today.AddDays(daysBefore)))
            .OfType<int>()
            .ToList();
        if (days.Count == 0)
        {
            return 0;
        }

        var slots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && days.Contains(s.Day) && s.AuthorName != null && s.PostUrl == null && s.Contact != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new { s.Id, s.Day, s.Part, AuthorName = s.AuthorName!, s.Contact!.Email, s.Season.SlotsPerDay })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var slot in slots)
        {
            var daysBefore = schedule.GetDate(year, slot.Day).DayNumber - today.DayNumber;
            var content = ReminderEmails.PostLinkMissing(
                new PostLinkReminder(slot.AuthorName, year, slot.Day, slot.Part, slot.SlotsPerDay, daysBefore),
                site.Value);
            if (await SendOnceAsync(ReminderKind.ContributorActionNeeded, schedule.InSeason(year, today), slot.Id, now, content, new EmailRecipient(slot.Email, slot.AuthorName), cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<int> SendDailySharesAsync(int year, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (schedule.GetDayOfAdvent(year, today) is not { } day)
        {
            return 0;
        }

        // Today's posts, revealed at midnight. Until one has its title and link there's nothing to share, and a later run
        // tries again.
        var revealed = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.Day == day && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .OrderBy(s => s.Part)
            .Select(s => new { s.Part, s.PostTitle, s.AuthorName })
            .ToListAsync(cancellationToken);

        // Links go through the tracked redirect, tagged as email.
        var posts = revealed
            .Select(s => new SharedPost(
                s.PostTitle!,
                EntryLinks.Absolute(site.Value.BaseUrl, EntryLinks.Go(year, day, s.Part, "email")),
                s.AuthorName!))
            .ToList();
        if (posts.Count == 0)
        {
            return 0;
        }

        var contacts = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.AuthorName != null && s.Contact != null)
            .Select(s => new { s.Id, s.Day, AuthorName = s.AuthorName!, s.Contact!.Email, OptedOut = s.Contact.DailyEmailsOptedOutAt != null })
            .ToListAsync(cancellationToken);

        // One email per person, however many spots they have, recorded against their first slot.
        var recipients = contacts
            .GroupBy(c => c.Email.Trim().ToLowerInvariant())
            .Where(person => !person.Any(c => c.OptedOut))
            .Select(person => person.OrderBy(c => c.Id).ToList())
            .Select(spots => new { First = spots[0], IsAuthorToday = spots.Any(c => c.Day == day) });

        var sent = 0;
        foreach (var recipient in recipients)
        {
            var content = ReminderEmails.Daily(
                // The day of Advent, not today's day of the month: they differ in the test season's rehearsal.
                new DailyShare(year, day, posts, recipient.First.AuthorName, recipient.IsAuthorToday),
                site.Value,
                optOut.CreateLink(recipient.First.Id));
            var to = new EmailRecipient(recipient.First.Email, recipient.First.AuthorName);
            if (await SendOnceAsync(ReminderKind.ContributorDailyShare, schedule.InSeason(year, today), recipient.First.Id, now, content, to, cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    /// <summary>
    /// Records the reminder, then sends it. If another run already recorded it, nothing is sent. If sending fails, the
    /// record is removed so a later run tries again.
    /// </summary>
    private async Task<bool> SendOnceAsync(
        ReminderKind kind,
        DateOnly runDate,
        int slotId,
        DateTimeOffset now,
        EmailContent content,
        EmailRecipient to,
        CancellationToken cancellationToken)
    {
        var record = new SentReminder { Kind = kind, RunDate = runDate, SlotId = slotId, SentAt = now };
        db.SentReminders.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(record).State = EntityState.Detached;
            return false;
        }

        try
        {
            await sender.SendAsync(await composer.ComposeAsync(content, to), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailed(exception, kind, slotId);
            db.SentReminders.Remove(record);
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }

        if (options.Value.PauseBetweenEmails > TimeSpan.Zero)
        {
            await Task.Delay(options.Value.PauseBetweenEmails, cancellationToken);
        }

        return true;
    }

    private DateOnly FirstDay(int year) => schedule.GetDate(year, 1);

    private DateOnly LastDay(int year) => schedule.GetDate(year, Season.DaysInAdvent);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send the {Kind} reminder for slot {SlotId}; a later run will try again")]
    private partial void LogSendFailed(Exception exception, ReminderKind kind, int slotId);
}
