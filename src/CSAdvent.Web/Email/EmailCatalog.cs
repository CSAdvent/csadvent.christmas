using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
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

    public static EmailCatalogEntry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);
}
