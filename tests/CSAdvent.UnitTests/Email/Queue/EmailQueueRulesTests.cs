using System.Net.Sockets;
using CSAdvent.Web.Announcements;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Reminders;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Email.Queue;

/// <summary>How the email queue (#152) ranks, names and describes emails.</summary>
public sealed class EmailQueueRulesTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static QueueingEmailSender Sender() => new(
        new NoTransport(),
        new EmailOptOutLinks(new EphemeralDataProtectionProvider(), Options.Create(Site)),
        new EmailQueueSignal(),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System,
        NullLogger<QueueingEmailSender>.Instance);

    private static OutgoingEmail Email(string to, bool transactional = false, EmailPriority? priority = null) =>
        new(new EmailRecipient(to), Site.ContactEmail, "Subject", "<p>Hi</p>", "Hi", null, transactional) { Priority = priority };

    [Fact]
    public void TransactionalEmailsAndTheOrganizersInboxGoFirst()
    {
        var sender = Sender();

        Assert.Equal(EmailPriority.Urgent, sender.PriorityOf(Email("ada@example.com", transactional: true)));
        Assert.Equal(EmailPriority.Normal, sender.PriorityOf(Email("ada@example.com")));

        // Whatever it asked for, the organizers' inbox goes first (a test announcement, say).
        Assert.Equal(EmailPriority.Urgent, sender.PriorityOf(Email("Hello@CSAdvent.christmas", priority: EmailPriority.Announcement)));

        // Transactional emails can ask to wait, as post link reminders do.
        Assert.Equal(EmailPriority.Normal, sender.PriorityOf(Email("ada@example.com", transactional: true, priority: EmailPriority.Normal)));
    }

    [Fact]
    public void ListMailKeepsTheOrderOfTheReadersPostsThenAnnouncementsThenInvitations()
    {
        var readers = ReminderEmails.DailyForReaders(3, [], Site, "https://csadvent.christmas/emails/announcements/daily-posts/stop?token=abc");
        var announcement = AnnouncementService.Email(new CSAdvent.Web.Data.Announcement { Subject = "News", Message = "News", CreatedBy = "ada" }, "https://csadvent.christmas/stop", Site);
        var invitation = PastAuthorEmails.Invitation(new PastAuthor("ada@example.com", "Ada", 2, 2019), 2026, Site);
        var authors = ReminderEmails.Daily(new DailyShare(2026, 3, [], "Ada", false), Site, "https://csadvent.christmas/emails/daily/stop?token=abc");
        var reminder = ReminderEmails.PostLinkMissing(new PostLinkReminder("Ada", 2026, 10, 1, 2, 5), Site);
        var lastCall = ReminderEmails.PostLinkDueToday(new DayOfReminder("Ada", 2026, 10, 1, 2, new TimeOnly(10, 0)), Site);

        Assert.Equal(EmailPriority.ReadersDailyPosts, readers.Priority);
        Assert.Equal(EmailPriority.Announcement, announcement.Priority);
        Assert.Equal(EmailPriority.Invitation, invitation.Priority);
        Assert.All([readers, announcement, invitation], email => Assert.True(email.Priority!.Value.IsListMail(), email.Kind));

        // The authors' daily email and post link reminders come after the urgent emails, but aren't list mail.
        Assert.Equal(EmailPriority.Normal, authors.Priority);
        Assert.Equal(EmailPriority.Normal, reminder.Priority);
        Assert.False(EmailPriority.Normal.IsListMail());

        // The last call is due within hours, so it's urgent (transactional, no priority of its own).
        Assert.True(lastCall.Transactional);
        Assert.Null(lastCall.Priority);
    }

    [Fact]
    public void EveryEmailIsQueuedUnderItsCatalogKey()
    {
        Assert.All(EmailCatalog.Entries, entry => Assert.Equal(entry.Key, entry.Sample(Site).Kind));
    }

    [Fact]
    public void FailuresAreDescribedWithoutAddresses()
    {
        var smtp = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.1.1 <ada@example.com>: Recipient address rejected");

        Assert.Equal("The SMTP server answered 550 (RecipientNotAccepted)", EmailFailures.Describe(smtp));
        Assert.Equal(EmailFailureKind.Failed, EmailFailures.Classify(smtp).Kind);
        Assert.Equal("The email service answered 400 (InvalidRecipient [address])", EmailFailures.Describe(new Azure.RequestFailedException(400, "Bad request: ada@example.com", "InvalidRecipient ada@example.com", null)));
        Assert.Equal("Couldn't reach the email service (ConnectionRefused)", EmailFailures.Describe(new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal("Sending took too long", EmailFailures.Describe(new TaskCanceledException()));
        Assert.Equal("Sending failed (InvalidOperationException)", EmailFailures.Describe(new InvalidOperationException("Can't send to ada@example.com")));
    }

    [Fact]
    public void TheEmailServiceSayingStopIsThrottling()
    {
        Assert.Equal(EmailFailureKind.Throttled, EmailFailures.Classify(new Azure.RequestFailedException(429, "Too many requests")).Kind);
        Assert.Equal(EmailFailureKind.Throttled, EmailFailures.Classify(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "Too many")).Kind);
        Assert.Equal(EmailFailureKind.Failed, EmailFailures.Classify(new Azure.RequestFailedException(503, "Unavailable")).Kind);
    }

    [Fact]
    public void TheLimitsMustLeaveListMailWithinTheHour()
    {
        Assert.Empty(Validate(new EmailQueueOptions()));
        Assert.NotEmpty(Validate(new EmailQueueOptions { PerMinute = 100, PerHour = 90 }));
        Assert.NotEmpty(Validate(new EmailQueueOptions { ListPerHour = 91 }));
        Assert.NotEmpty(Validate(new EmailQueueOptions { PollInterval = TimeSpan.Zero }));

        static List<System.ComponentModel.DataAnnotations.ValidationResult> Validate(EmailQueueOptions options)
        {
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new(options), results, validateAllProperties: true);
            return results;
        }
    }

    private sealed class NoTransport : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not in these tests.");
    }
}
