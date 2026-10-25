using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Announcements;

/// <summary>
/// Each December day's posts for announcement subscribers who asked for them (#206): the same email as the authors' daily
/// email, sent when it is (<see cref="DailyPosts"/>), with a link that turns off only the daily posts. It runs inside
/// <see cref="AnnouncementService.RunAsync"/>, first, and shares <see cref="AnnouncementOptions.PerHour"/> with
/// announcements and past authors' invitations, so it never adds to the hourly total. Each copy is recorded before it's sent,
/// so nobody gets a day twice even across replicas.
/// </summary>
/// <remarks>
/// A day's email only goes out on that day: a subscriber not reached by midnight (more than the hourly allowance allows) misses
/// it rather than getting yesterday's posts late. Never for the test season.
/// </remarks>
public sealed partial class ReaderDailyEmailService(
    AdventDbContext db,
    AdventSchedule schedule,
    DailyPosts dailyPosts,
    AnnouncementSubscriptions subscriptions,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<ReminderOptions> reminders,
    IOptions<SiteOptions> site,
    TimeProvider timeProvider,
    ILogger<ReaderDailyEmailService> logger)
{
    /// <summary>Daily posts emails sent after <paramref name="since"/> and up to <paramref name="until"/>, for the shared hourly allowance.</summary>
    public Task<int> CountSentAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken = default) =>
        db.DailyPostDeliveries.CountAsync(d => d.SentAt > since && d.SentAt <= until, cancellationToken);

    /// <summary>Confirmed subscribers who get the daily posts.</summary>
    public Task<int> CountReadersAsync(CancellationToken cancellationToken = default) =>
        db.AnnouncementSubscribers.CountAsync(s => s.ConfirmedAt != null && s.DailyPostsSince != null, cancellationToken);

    /// <summary>Sends up to <paramref name="budget"/> of today's emails, once the day's posts are due. Returns how many went out.</summary>
    public async Task<int> RunAsync(DateTimeOffset now, int budget, CancellationToken cancellationToken = default)
    {
        if (budget <= 0)
        {
            return 0;
        }

        // The real seasons in Advent today. There's one season per year, so this is a short list.
        var today = DateOnly.FromDateTime(schedule.ToLocalTime(now).DateTime);
        var years = (await db.Seasons.Where(s => s.Year != Season.TestYear).Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => schedule.GetDayOfAdvent(year, today) is not null)
            .ToList();

        var sent = 0;
        foreach (var year in years)
        {
            if (budget <= 0 || await dailyPosts.GetDueAsync(year, now, cancellationToken) is not { } edition)
            {
                continue;
            }

            var waiting = await db.AnnouncementSubscribers
                .AsNoTracking()
                .Where(s => s.ConfirmedAt != null && s.DailyPostsSince != null
                    && !db.DailyPostDeliveries.Any(d => d.SeasonYear == year && d.Day == edition.Day && d.SubscriberId == s.Id))
                .OrderBy(s => s.Id)
                .Take(budget)
                .Select(s => new { s.Id, s.Email })
                .ToListAsync(cancellationToken);
            foreach (var subscriber in waiting)
            {
                budget--;
                if (await DeliverAsync(edition, subscriber.Id, subscriber.Email, now, cancellationToken))
                {
                    sent++;
                    await Task.Delay(reminders.Value.PauseBetweenEmails, timeProvider, cancellationToken);
                }
            }
        }

        return sent;
    }

    private async Task<bool> DeliverAsync(DailyEdition edition, int subscriberId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Recorded first: the unique index means only one run or replica sends each copy.
        var delivery = new DailyPostDelivery { SeasonYear = edition.Year, Day = edition.Day, SubscriberId = subscriberId, SentAt = now };
        db.DailyPostDeliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            // Another run sent it, or they unsubscribed just now.
            db.Entry(delivery).State = EntityState.Detached;
            return false;
        }

        try
        {
            var content = ReminderEmails.DailyForReaders(edition.Day, edition.Posts, site.Value, subscriptions.DailyPostsStopLink(subscriberId));
            await sender.SendAsync(await composer.ComposeAsync(content, new EmailRecipient(email, email)), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the address: the logs never carry personal details. A later run tries again.
            LogDeliveryFailed(exception, edition.Year, edition.Day, subscriberId);
            await db.DailyPostDeliveries.Where(d => d.Id == delivery.Id).ExecuteDeleteAsync(cancellationToken);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send day {Day} of C# Advent {Year} to subscriber {SubscriberId}; a later run will try again")]
    private partial void LogDeliveryFailed(Exception exception, int year, int day, int subscriberId);
}
