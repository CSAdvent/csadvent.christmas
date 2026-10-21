using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;

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
    ];

    public static EmailCatalogEntry? Find(string key) => Entries.FirstOrDefault(e => e.Key == key);
}
