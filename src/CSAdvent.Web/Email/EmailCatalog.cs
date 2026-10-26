using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Reminders;

namespace CSAdvent.Web.Email;

/// <summary>An email the site sends: what triggers it, who gets it, and a sample for the preview page.</summary>
public sealed record EmailCatalogEntry(string Key, string Name, string When, string To, Func<SiteOptions, EmailContent> Sample)
{
    /// <summary>Whether it goes to the organizers' inbox, which gets no unsubscribe link.</summary>
    public bool IsForOrganizers => To.StartsWith("The organizers", StringComparison.Ordinal);
}

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

    private static readonly BlurbPost SampleBlurbPost = new(
        42,
        new BackupSpot(2026, 6, 1, 2),
        "Span<T> all the way down",
        "https://example.com/span",
        "10:00 AM Eastern on December 6");

    private static readonly PostClaimEmailDetails SamplePostClaim = new(
        RequestId: 7,
        Login: "ada-lovelace",
        Name: "Ada Lovelace",
        Note: "I wrote this under my old blog name, A. Byron.",
        Year: 2019,
        Day: 12,
        Part: 2,
        SlotsPerDay: 2,
        Title: "Pattern matching all the way down",
        Author: "A. Byron");

    private static readonly PostLinkEmailDetails SamplePostLinks = new(
        RequestId: 9,
        Login: "ada-lovelace",
        Name: "Ada Lovelace",
        Note: "I moved my blog from Blogger to my own domain.",
        Changes:
        [
            new(2021, 8, 1, 2, "Records, revisited", "https://ada.blogspot.com/2021/12/records-revisited.html", "https://ada.dev/records-revisited"),
            new(2019, 12, 2, 2, "Pattern matching all the way down", "https://ada.blogspot.com/2019/12/pattern-matching.html", "https://ada.dev/pattern-matching"),
        ]);

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
            "claim-approved-other-spot",
            "Request approved for another spot",
            "When an organizer approves a request into a different spot from the one asked for, such as when settling the other requests for a spot that's gone.",
            "The contributor. Replies go to the organizers.",
            site => ClaimEmails.Approved(SampleClaim with { Day = 5, Part = 2, RequestedDay = 3, RequestedPart = 1 }, site)),
        new(
            "claim-declined-taken",
            "Request declined: the spot's gone",
            "When an organizer declines a request whose spot has gone to someone else, while other days are still open.",
            "The contributor. Replies go to the organizers.",
            site => ClaimEmails.Declined(SampleClaim, ClaimDeclineReason.SpotTaken, site)),
        new(
            "claim-declined-full",
            "Request declined: the calendar's full",
            "When an organizer declines a request whose spot has gone to someone else, and every spot is taken.",
            "The contributor. Replies go to the organizers.",
            site => ClaimEmails.Declined(SampleClaim, ClaimDeclineReason.SeasonFull, site)),
        new(
            "claim-declined",
            "Request declined",
            "When an organizer declines a request whose spot is still open.",
            "The contributor. Replies go to the organizers.",
            site => ClaimEmails.Declined(SampleClaim, ClaimDeclineReason.NotApproved, site)),
        new(
            "post-claim-new-request",
            "Someone asked for an older post",
            "Right after a signed-in contributor asks for a finished season's post to be added to their account.",
            "The organizers (Site:ContactEmail). Replies go to the contributor.",
            site => PostClaimEmails.NewRequest(SamplePostClaim, site)),
        new(
            "post-claim-approved",
            "Older post added",
            "When an organizer approves a request to add an older post to someone's account.",
            "The contributor, at their verified GitHub email. Replies go to the organizers.",
            site => PostClaimEmails.Approved(SamplePostClaim, site)),
        new(
            "post-claim-declined",
            "Older post not added",
            "When an organizer declines a request to add an older post to someone's account.",
            "The contributor, at their verified GitHub email. Replies go to the organizers.",
            site => PostClaimEmails.Declined(SamplePostClaim, site)),
        new(
            "post-links-new-request",
            "Someone asked for new post links",
            "Right after a signed-in contributor asks for new links to posts on their account from finished seasons.",
            "The organizers (Site:ContactEmail). Replies go to the contributor.",
            site => PostLinkEmails.NewRequest(SamplePostLinks, site)),
        new(
            "post-links-approved",
            "Post links changed",
            "When an organizer approves a request for new links to someone's older posts.",
            "The contributor, at their verified GitHub email. Replies go to the organizers.",
            site => PostLinkEmails.Approved(SamplePostLinks, site)),
        new(
            "post-links-declined",
            "Post links not changed",
            "When an organizer declines a request for new links to someone's older posts.",
            "The contributor, at their verified GitHub email. Replies go to the organizers.",
            site => PostLinkEmails.Declined(SamplePostLinks, site)),
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
            "backup-ask-blurb",
            "Backup ask: the blurb is missing",
            "On a post's day, as soon as it's found without the C# Advent blurb (or an organizer asks for a backup), to the first backup in sign-up order; then the next every hour until the deadline (10 AM or 2 PM Eastern).",
            "One backup at a time.",
            site => BackupEmails.Ask("Grace Hopper", SampleSpot, BackupReason.BlurbMissing, "1:15 AM Eastern on December 12", SampleOffer(site), site, "10:00 AM Eastern on December 12")),
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
            "backup-replaced-blurb",
            "Spot went to a backup: no blurb",
            "When a backup's post replaces one that still didn't open with the C# Advent blurb at its deadline.",
            "The original author.",
            site => BackupEmails.Replaced("Ada Lovelace", SampleSpot, site, BackupReason.BlurbMissing)),
        new(
            "blurb-missing",
            "Please add the C# Advent blurb",
            "From a post's reveal (it's checked from the day before), once each time it's found without the blurb: no link to csadvent.christmas near the top, Claude says it's missing, or an organizer marks it missing.",
            "The author. Replies go to the organizers.",
            site => BlurbEmails.Missing("Ada Lovelace", SampleBlurbPost, "There's no link to csadvent.christmas near the top of the post.", site)),
        new(
            "blurb-needs-decision",
            "A post's blurb needs a decision",
            "From a post's reveal, when Claude is unsure about its blurb, or the post seems to talk to it. Once per post.",
            "The organizers.",
            site => BlurbEmails.NeedsDecision(SampleBlurbPost, CSAdvent.Web.Data.BlurbStatus.NeedsDecision, "Claude found the blurb, but its quote of it isn't in the post.", site)),
        new(
            "blurb-could-not-check",
            "A post couldn't be checked",
            "From a post's reveal, when its page hasn't loaded, or Claude hasn't answered, for 30 minutes. Once per post.",
            "The organizers.",
            site => BlurbEmails.NeedsDecision(SampleBlurbPost, CSAdvent.Web.Data.BlurbStatus.CouldNotCheck, "The post's page answered 404 Not Found.", site)),
        new(
            "blurb-deadline",
            "No blurb and no backup",
            "At a post's deadline (10 AM or 2 PM Eastern on its day), if it still has no blurb and no backup has taken the spot. Once per post.",
            "The organizers.",
            site => BlurbEmails.DeadlinePassed(SampleBlurbPost, site)),
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
            "announcements-confirm",
            "Confirm announcements",
            "When someone enters an address for announcements, unless it's already on the list or was sent one in the last day.",
            "The address entered.",
            site => CSAdvent.Web.Announcements.AnnouncementSubscriptions.Confirmation($"{site.BaseUrl.TrimEnd('/')}/emails/announcements/confirm?token=sample", site)),
        new(
            "announcements-confirm-daily",
            "Confirm the daily posts",
            "When someone enters an address for announcements and ticks the daily posts, unless it already gets them or was sent a confirmation in the last day. Subscribers asking for the daily posts get it too.",
            "The address entered.",
            site => CSAdvent.Web.Announcements.AnnouncementSubscriptions.Confirmation($"{site.BaseUrl.TrimEnd('/')}/emails/announcements/confirm?token=sample", site, dailyPosts: true)),
        new(
            "announcement",
            "Announcement",
            "When an organizer sends an announcement from /manage/announcements. It's queued for everyone at once, and goes after the emails people are waiting for, within the email queue's hourly allowance for list mail.",
            "Everyone on the announcements list who's confirmed their address. It has a one-click unsubscribe.",
            site => CSAdvent.Web.Announcements.AnnouncementService.Email(
                new CSAdvent.Web.Data.Announcement
                {
                    Subject = "The C# Advent 2026 calendar is full",
                    Message = "Every spot in C# Advent 2026 is taken. Thank you!\n\nYou can still join the backup list, and fill in if an author drops out.",
                    ButtonLabel = "Join the backup list",
                    ButtonUrl = $"{site.BaseUrl.TrimEnd('/')}/backups/2026",
                    CreatedBy = "CalvinAllen",
                },
                $"{site.BaseUrl.TrimEnd('/')}/emails/announcements/stop?token=sample",
                site)),
        new(
            "announcement-signups-open",
            "Sign-ups are open",
            "At 9 AM Eastern on the day the current season's sign-ups open, by itself. It's queued for everyone at once, and goes after the emails people are waiting for, within the email queue's hourly allowance for list mail.",
            "Everyone on the announcements list who's confirmed their address. It has a one-click unsubscribe.",
            site =>
            {
                var form = CSAdvent.Web.Announcements.AnnouncementService.SignUpsOpen(2026, site.BaseUrl);
                return CSAdvent.Web.Announcements.AnnouncementService.Email(
                    new CSAdvent.Web.Data.Announcement { Kind = CSAdvent.Web.Data.AnnouncementKind.SignUpsOpen, Subject = form.Subject, Message = form.Message, ButtonLabel = form.ButtonLabel, ButtonUrl = form.ButtonUrl, CreatedBy = "automation" },
                    $"{site.BaseUrl.TrimEnd('/')}/emails/announcements/stop?token=sample",
                    site);
            }),
        new(
            "past-author-invitation",
            "Invitation for past authors",
            "From 9 AM Eastern on the day the current season's sign-ups open, by itself, after the announcement: only as many at a time as the email queue has room for, until everyone's had one or sign-ups close.",
            "Everyone who has written for C# Advent before, once per address, except announcement subscribers, anyone who already has a spot or request, and anyone who unsubscribed. It has the unsubscribe link.",
            site => CSAdvent.Web.Announcements.PastAuthorEmails.Invitation(new CSAdvent.Web.Announcements.PastAuthor("ada@example.com", "Ada Lovelace", 3, 2019), 2026, site)),
        new(
            "daily-share",
            "Today's posts",
            "Each day from December 1 to 25: from 9 AM Eastern once every post of the day has its title and link, or from 2 PM Eastern (the day's last announcement) with whichever do.",
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
        new(
            "daily-posts-readers",
            "Today's posts, for readers",
            "Each day from December 1 to 25, when the authors' daily email goes (once both posts are in, from 9 AM Eastern, 2 PM at the latest). It's queued for every reader at once and goes after the authors', within the email queue's hourly allowance for list mail, until midnight.",
            "Announcement subscribers who asked for the daily posts, once each. It has a one-click link that turns off only the daily posts.",
            site => ReminderEmails.DailyForReaders(
                3,
                [
                    new SharedPost("Ten things I wish I knew about Span<T>", "https://example.com/span", "Ada Lovelace"),
                    new SharedPost("Source generators from scratch", "https://example.com/generators", "Grace Hopper"),
                ],
                site,
                $"{site.BaseUrl.TrimEnd('/')}/emails/announcements/daily-posts/stop?token=sample")),
    ];

    private static string SampleOffer(SiteOptions site) => $"{site.BaseUrl.TrimEnd('/')}/backups/offers/7";

    public static EmailCatalogEntry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);
}
