using CSAdvent.Web.Announcements;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Email;
using CSAdvent.Web.Reminders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Email;

public sealed class OptOutAwareEmailSenderTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private sealed class RecordingSender : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = [];

        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class OptedOut(params string[] addresses) : IEmailOptOutList
    {
        public Task<bool> IsOptedOutAsync(string address, CancellationToken cancellationToken = default) =>
            Task.FromResult(addresses.Contains(EmailOptOutLinks.Normalize(address)));
    }

    private static (OptOutAwareEmailSender Sender, RecordingSender Transport) Create(params string[] optedOut)
    {
        var transport = new RecordingSender();
        var services = new ServiceCollection().AddScoped<IEmailOptOutList>(_ => new OptedOut(optedOut)).BuildServiceProvider();
        var sender = new OptOutAwareEmailSender(
            transport,
            new EmailOptOutLinks(new EphemeralDataProtectionProvider(), Options.Create(Site)),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OptOutAwareEmailSender>.Instance);
        return (sender, transport);
    }

    private static OutgoingEmail Email(string to, bool transactional) =>
        new(new EmailRecipient(to), Site.ContactEmail, "Subject", "<p>Hi</p>", "Hi", null, transactional);

    [Fact]
    public async Task OptionalEmailsSkipSomeoneWhoUnsubscribed()
    {
        var (sender, transport) = Create("ada@example.com");

        await sender.SendAsync(Email("Ada@Example.com", transactional: false), TestContext.Current.CancellationToken);
        await sender.SendAsync(Email("grace@example.com", transactional: false), TestContext.Current.CancellationToken);

        Assert.Equal(["grace@example.com"], transport.Sent.Select(e => e.To.Address));
    }

    [Fact]
    public async Task EmailsAboutWhatTheySignedUpForAlwaysGo()
    {
        var (sender, transport) = Create("ada@example.com");

        await sender.SendAsync(Email("ada@example.com", transactional: true), TestContext.Current.CancellationToken);

        Assert.Single(transport.Sent);
    }

    [Fact]
    public async Task TheOrganizersInboxIsNeverSkipped()
    {
        var (sender, transport) = Create("hello@csadvent.christmas");

        await sender.SendAsync(Email("hello@csadvent.christmas", transactional: false), TestContext.Current.CancellationToken);

        Assert.Single(transport.Sent);
    }

    [Fact]
    public void EmailsAboutSpotsRequestsAndBackupOffersAreTransactionalAndTheRestAreOptional()
    {
        var claim = new ClaimEmailDetails(42, "Ada", "ada@example.com", "ada", null, null, 2026, 3, 1, 2);
        var spot = new BackupSpot(2026, 12, 2, 2);
        var postClaim = new PostClaimEmailDetails(7, "ada", "Ada", null, 2019, 12, 2, 2, "Title", "Ada");
        var postLinks = new PostLinkEmailDetails(9, "ada", "Ada", null, [new PostLinkEmailChange(2019, 12, 2, 2, "Title", "https://old.example.com", "https://new.example.com")]);

        Assert.All(
            [
                ClaimEmails.Received(claim, Site),
                ClaimEmails.Approved(claim, Site),
                ClaimEmails.Approved(claim with { Day = 5, RequestedDay = 3, RequestedPart = 1 }, Site),
                ClaimEmails.Declined(claim, ClaimDeclineReason.SpotTaken, Site),
                ClaimEmails.Declined(claim, ClaimDeclineReason.SeasonFull, Site),
                ClaimEmails.Declined(claim, ClaimDeclineReason.NotApproved, Site),
                BackupEmails.SignedUp("Grace", 2026, 1, Site),
                BackupEmails.Ask("Grace", spot, BackupReason.DroppedOut, "1 PM", "https://csadvent.christmas/backups/offers/1", Site),
                BackupEmails.YoureOn("Grace", spot, "Post", Site),
                BackupEmails.NotNeeded("Grace", spot, Site),
                BackupEmails.Replaced("Ada", spot, Site),
                BackupEmails.Ask("Grace", spot, BackupReason.BlurbMissing, "1 AM", "https://csadvent.christmas/backups/offers/1", Site, "10 AM"),
                BackupEmails.Replaced("Ada", spot, Site, BackupReason.BlurbMissing),
                BlurbEmails.Missing("Ada", new BlurbPost(1, spot, "Post", "https://example.com/post", "10 AM"), "It's missing.", Site),
                PostClaimEmails.Approved(postClaim, Site),
                PostClaimEmails.Declined(postClaim, Site),
                PostLinkEmails.Approved(postLinks, Site),
                PostLinkEmails.Declined(postLinks, Site),
                ReminderEmails.PostLinkMissing(new PostLinkReminder("Ada", 2026, 10, 1, 2, 3), Site),
                ReminderEmails.PostLinkDueToday(new DayOfReminder("Ada", 2026, 10, 1, 2, new TimeOnly(10, 0)), Site),
            ],
            email => Assert.True(email.Transactional, email.Subject));

        Assert.All(
            [
                AnnouncementSubscriptions.Confirmation("https://csadvent.christmas/emails/announcements/confirm?token=abc", Site),
                AnnouncementSubscriptions.Confirmation("https://csadvent.christmas/emails/announcements/confirm?token=abc", Site, dailyPosts: true),
                ReminderEmails.Daily(new DailyShare(2026, 3, [], "Ada", false), Site, "https://csadvent.christmas/emails/daily/stop?token=abc"),
                ReminderEmails.DailyForReaders(3, [], Site, "https://csadvent.christmas/emails/announcements/daily-posts/stop?token=abc"),

                // To the organizers' inbox, which is never skipped and gets no unsubscribe link.
                BlurbEmails.BackupStandingBy(new BlurbPost(1, spot, "Post", "https://example.com/post", "10 AM"), new BlurbBackup("Grace", "Hers", "https://example.com/hers"), "It's missing.", "10 AM", Site),
            ],
            email => Assert.False(email.Transactional, email.Subject));
    }
}
