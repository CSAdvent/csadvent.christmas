using CSAdvent.Web.Backups;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Reminders;

namespace CSAdvent.Web.Email;

/// <summary>An email the site sends: what triggers it, who gets it, and a sample for the preview page.</summary>
public sealed record EmailCatalogEntry(string Key, string Name, string When, string To, Func<SiteOptions, EmailContent> Sample);

/// <summary>
/// Every email the site sends, for review on the development preview page (<c>/dev/emails</c>). Add new emails here
/// along with their senders, so the list stays complete.
/// </summary>
public static class EmailCatalog
{
    private static readonly ClaimEmailDetails SampleClaim = new(
        RequestId: 42,
        DisplayName: "Ada Lovelace",
        Email: "ada@example.com",
        GithubLogin: "ada-lovelace",
        Link: "https://example.com/ada",
        Comments: "December 5 works too.",
        Year: 2026,
        Day: 3,
        Part: 1,
        SlotsPerDay: 2);

    private static readonly BackupSpot SampleSpot = new(2026, 12, 2, 2);

    public static IReadOnlyList<EmailCatalogEntry> Entries { get; } =
    [
        new(
            "claim-received",
            "Request received",
            "Right after a contributor submits the claim form.",
            "The contributor, at their verified GitHub email. Replies go to the organizers.",
            site => ClaimEmails.Received(SampleClaim, site)),
        new(
            "claim-new-request",
            "New request",
            "Right after a contributor submits the claim form.",
            "The organizers (Site:ContactEmail). Replies go to the contributor.",
            site => ClaimEmails.NewRequest(SampleClaim, site)),
        new(
            "claim-approved",
            "Request approved",
            "When an organizer approves a request into a spot.",
            "The contributor. Replies go to the organizers.",
            site => ClaimEmails.Approved(SampleClaim, site)),
        new(
            "dropped-out",
            "An author dropped out",
            "When an author gives up their spot from their My spots page.",
            "The organizers (Site:ContactEmail).",
            site => SpotEmails.DroppedOut(new DropoutDetails("Ada Lovelace", 2026, 3, 1, 2), site)),
        new(
            "backup-signed-up",
            "Joined the backup list",
            "Right after someone joins a season's backup list.",
            "The new backup. Replies go to the organizers.",
            site => BackupEmails.SignedUp("Grace Hopper", 2026, 3, site)),
        new(
            "backup-ask-dropped",
            "Backup ask: an author dropped out",
            "As soon as an author drops out (or an organizer releases a spot), to the first backup in sign-up order. Each backup gets 4 hours before the next is asked too.",
            "One backup at a time.",
            site => BackupEmails.Ask("Grace Hopper", SampleSpot, BackupReason.DroppedOut, "1:30 PM Eastern on November 20", SampleOffer(site), site)),
        new(
            "backup-ask-missing",
            "Backup ask: no post link yet",
            "At 6 PM Eastern the evening before a spot's day if it still has no post link, to the first backup in sign-up order; then the next every 4 hours.",
            "One backup at a time.",
            site => BackupEmails.Ask("Grace Hopper", SampleSpot, BackupReason.LinkMissing, "10:00 PM Eastern on December 11", SampleOffer(site), site)),
        new(
            "backup-used",
            "Backup's post is on the calendar",
            "When a backup's post replaces a spot's: straight away for a dropped spot, or at the reveal for a missing link.",
            "The backup.",
            site => BackupEmails.YoureOn("Grace Hopper", SampleSpot, "Collection expressions everywhere", site)),
        new(
            "backup-not-needed",
            "Backup not needed after all",
            "When the original author's link arrives after a backup accepted.",
            "The backup who accepted.",
            site => BackupEmails.NotNeeded("Grace Hopper", SampleSpot, site)),
        new(
            "backup-spot-filled",
            "A backup filled a spot",
            "When a backup's post goes on the calendar.",
            "The organizers.",
            site => BackupEmails.SpotFilled("Grace Hopper", SampleSpot, BackupReason.LinkMissing, site)),
        new(
            "backup-none-left",
            "No backups left",
            "When every backup has been asked about a spot and nobody took it. Once per spot.",
            "The organizers.",
            site => BackupEmails.NoBackups(SampleSpot, site)),
        new(
            "backup-replaced",
            "Spot went to a backup",
            "When a backup's post replaces an author whose link didn't arrive before the reveal.",
            "The original author.",
            site => BackupEmails.Replaced("Ada Lovelace", SampleSpot, site)),
        new(
            "post-link-reminder",
            "Post link reminder",
            "At 9 AM Eastern 7, 5 and 3 days before an author's day, if their spot has no post link yet.",
            "The author. Replies go to the organizers.",
            site => ReminderEmails.PostLinkMissing(new PostLinkReminder("Ada Lovelace", 2026, 10, 1, 2, DaysBefore: 5), site)),
        new(
            "post-link-reminder-final",
            "Post link reminder, with the backup warning",
            "At 9 AM Eastern 2 days and 1 day before an author's day, if their spot still has no post link.",
            "The author. Replies go to the organizers.",
            site => ReminderEmails.PostLinkMissing(new PostLinkReminder("Ada Lovelace", 2026, 10, 1, 2, DaysBefore: 1), site)),
        new(
            "post-link-day-of",
            "Post link reminder, on the day",
            "On an author's day, 3 hours before their post is announced (7 AM Eastern for post 1, 11 AM for post 2), if their spot still has no post link.",
            "The author. Replies go to the organizers.",
            site => ReminderEmails.PostLinkDueToday(new DayOfReminder("Ada Lovelace", 2026, 10, 1, 2, new TimeOnly(10, 0)), site)),
        new(
            "social-credentials-failing",
            "Automatic posting has stopped working",
            "At 8 AM Eastern, when a platform in Automatic mode fails the daily check of its credentials (once a day per platform).",
            "The organizers.",
            site => CSAdvent.Web.Social.Posting.SocialCredentialCheck.CredentialsFailing(
                CSAdvent.Web.Claims.SocialPlatform.Facebook, "401 Unauthorized: Error validating access token: The session has been invalidated.", site)),
        new(
            "daily-share",
            "Today's posts",
            "At 9 AM Eastern each day from December 1 to 25, once at least one of the day's posts has its title and link.",
            "Everyone with a spot that season, once each, unless they turned it off with the link in the email.",
            site => ReminderEmails.Daily(
                new DailyShare(
                    2026,
                    3,
                    [
                        new SharedPost("Ten things I wish I knew about Span<T>", "https://example.com/span", "Ada Lovelace"),
                        new SharedPost("Source generators from scratch", "https://example.com/generators", "Grace Hopper"),
                    ],
                    "Ada Lovelace",
                    RecipientIsAuthorToday: true),
                site,
                $"{site.BaseUrl.TrimEnd('/')}/emails/daily/stop?token=sample")),
    ];

    private static string SampleOffer(SiteOptions site) => $"{site.BaseUrl.TrimEnd('/')}/backups/offers/7";

    public static EmailCatalogEntry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);
}
