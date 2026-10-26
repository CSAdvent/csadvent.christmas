using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Announcements;

/// <summary>How a day's posts for readers are getting on in the email queue.</summary>
public sealed record ReaderDayProgress(int Year, int Day, QueueProgress Queue);

/// <summary>
/// Each December day's posts for announcement subscribers who asked for them (#206): the same email as the authors' daily
/// email, due when it is (<see cref="DailyPosts"/>), with a link that turns off only the daily posts. It runs inside
/// <see cref="AnnouncementService.RunAsync"/>, first, and queues the day's copies all at once: the email queue (#152) sends
/// them after the authors' daily email and before announcements and invitations, as list mail within
/// <see cref="EmailQueueOptions.ListPerHour"/>. Each copy is recorded before it's queued, so nobody gets a day twice even
/// across replicas.
/// </summary>
/// <remarks>
/// A day's email only goes out on that day: one the queue hasn't sent by midnight expires, rather than bring yesterday's posts
/// late. Turning off the daily posts takes back any still waiting. Never for the test season.
/// </remarks>
public sealed partial class ReaderDailyEmailService(
    AdventDbContext db,
    AdventSchedule schedule,
    DailyPosts dailyPosts,
    AnnouncementSubscriptions subscriptions,
    EmailComposer composer,
    IEmailSender sender,
    EmailOutbox outbox,
    IOptions<SiteOptions> site,
    ILogger<ReaderDailyEmailService> logger)
{
    /// <summary>What the email queue knows a day's copies by (<see cref="EmailOutbox.GetProgressAsync"/>, unsubscribing).</summary>
    public static string Reference(int year, int day) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"daily-posts:{year}:{day}");

    /// <summary>Every day's copies, for taking back the ones still waiting when someone turns the daily posts off.</summary>
    public const string ReferencePrefix = "daily-posts:";

    /// <summary>
    /// How the latest day's copies are getting on in the email queue, for the organizers: the most recent day the queue still
    /// has copies of (it keeps them a week), or null if it has none.
    /// </summary>
    public async Task<ReaderDayProgress?> GetLatestProgressAsync(CancellationToken cancellationToken = default)
    {
        var days = (await outbox.GetProgressAsync(ReferencePrefix, cancellationToken))
            .Select(entry => entry.Key.Split(':') is [_, var year, var day]
                && int.TryParse(year, System.Globalization.CultureInfo.InvariantCulture, out var y)
                && int.TryParse(day, System.Globalization.CultureInfo.InvariantCulture, out var d)
                    ? new ReaderDayProgress(y, d, entry.Value)
                    : null)
            .OfType<ReaderDayProgress>();
        return days.OrderByDescending(p => p.Year).ThenByDescending(p => p.Day).FirstOrDefault();
    }

    /// <summary>Confirmed subscribers who get the daily posts.</summary>
    public Task<int> CountReadersAsync(CancellationToken cancellationToken = default) =>
        db.AnnouncementSubscribers.CountAsync(s => s.ConfirmedAt != null && s.DailyPostsSince != null, cancellationToken);

    /// <summary>Queues today's emails for every reader who hasn't had theirs, once the day's posts are due. Returns how many were queued.</summary>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // The real seasons in Advent today. There's one season per year, so this is a short list.
        var today = DateOnly.FromDateTime(schedule.ToLocalTime(now).DateTime);
        var years = (await db.Seasons.Where(s => s.Year != Season.TestYear).Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => schedule.GetDayOfAdvent(year, today) is not null)
            .ToList();

        var queued = 0;
        foreach (var year in years)
        {
            if (await dailyPosts.GetDueAsync(year, now, cancellationToken) is not { } edition)
            {
                continue;
            }

            var waiting = await db.AnnouncementSubscribers
                .AsNoTracking()
                .Where(s => s.ConfirmedAt != null && s.DailyPostsSince != null
                    && !db.DailyPostDeliveries.Any(d => d.SeasonYear == year && d.Day == edition.Day && d.SubscriberId == s.Id))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.Email })
                .ToListAsync(cancellationToken);
            foreach (var subscriber in waiting)
            {
                if (await DeliverAsync(edition, subscriber.Id, subscriber.Email, now, cancellationToken))
                {
                    queued++;
                }
            }
        }

        return queued;
    }

    private async Task<bool> DeliverAsync(DailyEdition edition, int subscriberId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Recorded first: the unique index means only one run or replica queues each copy.
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
            var content = ReminderEmails.DailyForReaders(edition.Day, edition.Posts, site.Value, subscriptions.DailyPostsStopLink(subscriberId)) with
            {
                SendBefore = schedule.GetDayEndTime(edition.Year, edition.Day),
                Reference = Reference(edition.Year, edition.Day),
            };
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue day {Day} of C# Advent {Year} for subscriber {SubscriberId}; a later run will try again")]
    private partial void LogDeliveryFailed(Exception exception, int year, int day, int subscriberId);
}
