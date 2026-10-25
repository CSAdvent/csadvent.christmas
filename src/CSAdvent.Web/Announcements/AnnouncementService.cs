using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Announcements;

/// <summary>An announcement in the organizers' list, with how far its delivery has got.</summary>
public sealed record AnnouncementRow(int Id, AnnouncementKind Kind, string Subject, string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? FinishedAt, int Delivered, int Recipients)
{
    public bool IsDraft => SentAt is null;
}

/// <param name="DailyPosts">Confirmed subscribers who get each December day's posts too (#206).</param>
public sealed record SubscriberCounts(int Confirmed, int Unconfirmed, int DailyPosts = 0);

public sealed class AnnouncementOptions
{
    public const string SectionName = "Announcements";

    /// <summary>
    /// How many emails to the list go out an hour (announcements, past authors' invitations and readers' daily posts together),
    /// leaving room under the email service's limit (100 an hour for a new custom domain) for reminders and the rest.
    /// </summary>
    [Range(1, 10000)]
    public int PerHour { get; set; } = 40;
}

/// <summary>
/// Announcements to the list (#49): organizers write them at <c>/manage/announcements</c>, and the season's "sign-ups are
/// open" email is queued automatically, when past authors start being invited too (<see cref="PastAuthorInvitationService"/>).
/// <see cref="AnnouncementWorker"/> delivers them a few at a time, within <see cref="AnnouncementOptions.PerHour"/> for every
/// email to the list together (readers' daily posts too, <see cref="ReaderDailyEmailService"/>), recording each copy before
/// sending it, so nobody gets one twice even across replicas.
/// </summary>
public sealed partial class AnnouncementService(
    AdventDbContext db,
    AdventSchedule schedule,
    SeasonAdminService seasons,
    AnnouncementSubscriptions subscriptions,
    PastAuthorInvitationService invitations,
    ReaderDailyEmailService readers,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<AnnouncementOptions> options,
    IOptions<ReminderOptions> reminders,
    IOptions<SiteOptions> site,
    TimeProvider timeProvider,
    ILogger<AnnouncementService> logger)
{
    public const string Automation = "automation";

    /// <summary>The "sign-ups are open" email is only queued this soon after sign-ups open, so creating a season late doesn't send a stale one.</summary>
    public static readonly TimeSpan SignUpsAnnouncedWithin = TimeSpan.FromDays(2);

    public async Task<IReadOnlyList<AnnouncementRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Announcements
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnnouncementRow(
                a.Id,
                a.Kind,
                a.Subject,
                a.CreatedBy,
                a.CreatedAt,
                a.SentAt,
                a.FinishedAt,
                db.AnnouncementDeliveries.Count(d => d.AnnouncementId == a.Id),
                a.SentAt == null
                    ? db.AnnouncementSubscribers.Count(s => s.ConfirmedAt != null)
                    : db.AnnouncementDeliveries.Count(d => d.AnnouncementId == a.Id)
                        + db.AnnouncementSubscribers.Count(s => s.ConfirmedAt != null && s.ConfirmedAt <= a.SentAt
                            && !db.AnnouncementDeliveries.Any(d => d.AnnouncementId == a.Id && d.SubscriberId == s.Id))))
            .ToListAsync(cancellationToken);

    public Task<Announcement?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.Announcements.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<SubscriberCounts> CountSubscribersAsync(CancellationToken cancellationToken = default) => new(
        await db.AnnouncementSubscribers.CountAsync(s => s.ConfirmedAt != null, cancellationToken),
        await db.AnnouncementSubscribers.CountAsync(s => s.ConfirmedAt == null, cancellationToken),
        await readers.CountReadersAsync(cancellationToken));

    /// <summary>Saves a draft, new or existing. Returns its ID, or null if it's been sent and can't change.</summary>
    public async Task<int?> SaveDraftAsync(int? id, AnnouncementForm form, string savedBy, CancellationToken cancellationToken = default)
    {
        Announcement? announcement;
        if (id is { } existing)
        {
            announcement = await db.Announcements.SingleOrDefaultAsync(a => a.Id == existing && a.SentAt == null, cancellationToken);
            if (announcement is null)
            {
                return null;
            }
        }
        else
        {
            announcement = new Announcement { Kind = AnnouncementKind.Organizer, Subject = "", Message = "", CreatedAt = timeProvider.GetUtcNow(), CreatedBy = savedBy };
            db.Announcements.Add(announcement);
        }

        announcement.Subject = form.Subject.Trim();
        announcement.Message = form.Message.Trim();
        announcement.ButtonLabel = Clean(form.ButtonLabel);
        announcement.ButtonUrl = Clean(form.ButtonUrl);
        await db.SaveChangesAsync(cancellationToken);
        return announcement.Id;
    }

    /// <summary>Sends the announcement to the organizers' inbox, marked as a test, so it can be checked before it goes out.</summary>
    public async Task<bool> SendTestAsync(int id, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(id, cancellationToken) is not { } announcement)
        {
            return false;
        }

        var content = Email(announcement, $"{site.Value.BaseUrl.TrimEnd('/')}{AnnouncementSubscriptions.StopPath}?token=test", site.Value);
        try
        {
            await sender.SendAsync(
                await composer.ComposeAsync(content with { Subject = $"[Test] {content.Subject}" }, new EmailRecipient(site.Value.ContactEmail, "C# Advent organizers")),
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTestFailed(exception, id);
            return false;
        }
    }

    /// <summary>Sends a draft to the list: every subscriber confirmed by now gets it. Returns false if it's already been sent.</summary>
    public async Task<bool> SendAsync(int id, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return await db.Announcements
            .Where(a => a.Id == id && a.SentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SentAt, now), cancellationToken) == 1;
    }

    public async Task<bool> DeleteDraftAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Announcements.Where(a => a.Id == id && a.SentAt == null).ExecuteDeleteAsync(cancellationToken) == 1;

    /// <summary>
    /// Queues the "sign-ups are open" email when it's due, then sends what it can within the hour's allowance: the day's posts
    /// to readers first (they're only news that day), then announcements, then past authors' invitations while the season's
    /// sign-ups are open. Returns how many emails went out.
    /// </summary>
    /// <param name="signUpsYear">The season whose sign-ups to announce, instead of the current one (for the tests).</param>
    public async Task<int> RunAsync(DateTimeOffset now, int? signUpsYear = null, CancellationToken cancellationToken = default)
    {
        var year = signUpsYear ?? await seasons.GetCurrentYearAsync(cancellationToken);
        if (await SignUpsOpeningAsync(now, year, cancellationToken) is { } openingYear)
        {
            await QueueSignUpsOpenAsync(openingYear, now, cancellationToken);
        }

        // Only the hour up to now counts, so a run at another time (the tests' runs in other years) never uses it up.
        var hourAgo = now - TimeSpan.FromHours(1);
        var budget = options.Value.PerHour
            - await db.AnnouncementDeliveries.CountAsync(d => d.SentAt > hourAgo && d.SentAt <= now, cancellationToken)
            - await invitations.CountSentAsync(hourAgo, now, cancellationToken)
            - await readers.CountSentAsync(hourAgo, now, cancellationToken);
        var sent = await readers.RunAsync(now, budget, cancellationToken);
        budget -= sent;
        foreach (var announcement in await db.Announcements.AsNoTracking().Where(a => a.SentAt != null && a.FinishedAt == null).OrderBy(a => a.SentAt).ToListAsync(cancellationToken))
        {
            if (budget <= 0)
            {
                break;
            }

            var waiting = await db.AnnouncementSubscribers
                .AsNoTracking()
                .Where(s => s.ConfirmedAt != null && s.ConfirmedAt <= announcement.SentAt
                    && !db.AnnouncementDeliveries.Any(d => d.AnnouncementId == announcement.Id && d.SubscriberId == s.Id))
                .OrderBy(s => s.Id)
                .Take(budget)
                .Select(s => new { s.Id, s.Email })
                .ToListAsync(cancellationToken);
            if (waiting.Count == 0)
            {
                await db.Announcements.Where(a => a.Id == announcement.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.FinishedAt, now), cancellationToken);
                continue;
            }

            foreach (var subscriber in waiting)
            {
                budget--;
                if (await DeliverAsync(announcement, subscriber.Id, subscriber.Email, now, cancellationToken))
                {
                    sent++;
                    await Task.Delay(reminders.Value.PauseBetweenEmails, timeProvider, cancellationToken);
                }
            }
        }

        // The invitations decide for themselves whether they're going out: from the same morning, until sign-ups close.
        if (year is { } invitingYear)
        {
            sent += await invitations.RunAsync(invitingYear, now, budget, cancellationToken);
        }

        return sent;
    }

    /// <summary>The announcement as an email, with the subscriber's own unsubscribe link (also its one-click header).</summary>
    public static EmailContent Email(Announcement announcement, string unsubscribeUrl, SiteOptions site)
    {
        List<EmailBlock> body = [.. Paragraphs(announcement.Message).Select(text => (EmailBlock)Paragraph(text))];
        if (announcement.ButtonLabel is { } label && announcement.ButtonUrl is { } url)
        {
            body.Add(Button(label, url));
        }

        body.Add(Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers"));
        return new EmailContent
        {
            Subject = announcement.Subject,
            Preview = Paragraphs(announcement.Message).FirstOrDefault() ?? announcement.Subject,
            Heading = announcement.Subject,
            Body = body,
            Reason = "You're getting this email because you signed up for C# Advent's announcements.",
            UnsubscribeUrl = unsubscribeUrl,
        };
    }

    /// <summary>The text of the season's automatic "sign-ups are open" email.</summary>
    public static AnnouncementForm SignUpsOpen(int year, string baseUrl) => new()
    {
        Subject = $"C# Advent {year} sign-ups are open",
        Message =
            $"Sign-ups for C# Advent {year} are open! Pick a day in December, claim it, and write a post about C# for the " +
            "community. Two posts go up each day from December 1 to 25.\n\n" +
            "Spots go quickly, so don't wait too long. Can't commit to a day? Join the backup list and fill in if an author drops out.",
        ButtonLabel = "Claim your spot",
        ButtonUrl = baseUrl.TrimEnd('/') + "/",
    };

    /// <summary>The message's paragraphs: separated by blank lines, each on one line.</summary>
    public static IReadOnlyList<string> Paragraphs(string message) =>
        [.. message.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => string.Join(' ', paragraph.Split('\n', StringSplitOptions.TrimEntries)))];

    /// <summary>
    /// The season whose sign-ups have just opened, if it's time to say so: only the current season, never the test season,
    /// from the morning send time once sign-ups are open, and for <see cref="SignUpsAnnouncedWithin"/>.
    /// </summary>
    private async Task<int?> SignUpsOpeningAsync(DateTimeOffset now, int? current, CancellationToken cancellationToken)
    {
        if (current is not { } year || year == Season.TestYear
            || await db.Seasons.Where(s => s.Year == year).Select(s => s.ClaimsOpenAt).SingleAsync(cancellationToken) is not { } opensAt
            || now < opensAt || now - opensAt > SignUpsAnnouncedWithin
            || TimeOnly.FromDateTime(schedule.ToLocalTime(now).DateTime) < reminders.Value.SendAt)
        {
            return null;
        }

        return year;
    }

    private async Task QueueSignUpsOpenAsync(int year, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var form = SignUpsOpen(year, site.Value.BaseUrl);
        db.Announcements.Add(new Announcement
        {
            Kind = AnnouncementKind.SignUpsOpen,
            SeasonYear = year,
            Subject = form.Subject,
            Message = form.Message,
            ButtonLabel = form.ButtonLabel,
            ButtonUrl = form.ButtonUrl,
            CreatedAt = now,
            CreatedBy = Automation,
            SentAt = now,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Already queued, by an earlier run or another replica.
            db.ChangeTracker.Clear();
        }
    }

    private async Task<bool> DeliverAsync(Announcement announcement, int subscriberId, string email, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Recorded first: the unique index means only one run or replica sends each copy.
        var delivery = new AnnouncementDelivery { AnnouncementId = announcement.Id, SubscriberId = subscriberId, SentAt = now };
        db.AnnouncementDeliveries.Add(delivery);
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
            var content = Email(announcement, subscriptions.StopLink(subscriberId), site.Value);
            await sender.SendAsync(await composer.ComposeAsync(content, new EmailRecipient(email, email)), cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the address: the logs never carry personal details. A later run tries again.
            LogDeliveryFailed(exception, announcement.Id, subscriberId);
            await db.AnnouncementDeliveries.Where(d => d.Id == delivery.Id).ExecuteDeleteAsync(cancellationToken);
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send the test of announcement {AnnouncementId}")]
    private partial void LogTestFailed(Exception exception, int announcementId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send announcement {AnnouncementId} to subscriber {SubscriberId}; a later run will try again")]
    private partial void LogDeliveryFailed(Exception exception, int announcementId, int subscriberId);
}

/// <summary>What an organizer writes: a subject, the message (blank lines between paragraphs), and an optional button.</summary>
public sealed class AnnouncementForm : IValidatableObject
{
    [Required(ErrorMessage = "Enter a subject.")]
    [StringLength(150, ErrorMessage = "Keep the subject to 150 characters or fewer.")]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "Write the message.")]
    [StringLength(5000, ErrorMessage = "Keep the message to 5,000 characters or fewer.")]
    public string Message { get; set; } = "";

    [StringLength(60, ErrorMessage = "Keep the button's label to 60 characters or fewer.")]
    public string? ButtonLabel { get; set; }

    [StringLength(500, ErrorMessage = "Keep the button's link to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "The button's link must be a full web address that starts with https:// or http://.")]
    public string? ButtonUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ButtonLabel) != string.IsNullOrWhiteSpace(ButtonUrl))
        {
            yield return new ValidationResult("Give the button both a label and a link, or neither.", [nameof(ButtonLabel), nameof(ButtonUrl)]);
        }
    }
}
