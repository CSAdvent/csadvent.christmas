using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Claims;

/// <summary>A claim request, as the emails about it need it.</summary>
/// <param name="RequestedDay">The day they asked for, when an organizer approved them into a different spot; otherwise 0.</param>
public sealed record ClaimEmailDetails(
    int RequestId,
    string DisplayName,
    string Email,
    string GithubLogin,
    string? Link,
    string? Comments,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    int RequestedDay = 0,
    int RequestedPart = 0)
{
    /// <summary>The spot, such as "December 3, 2026 (post 1)". The post number only matters when a day has more than one.</summary>
    public string Spot => Name(Day, Part);

    /// <summary>The spot they asked for, if it isn't the one they got.</summary>
    public string? AskedFor => RequestedDay > 0 && (RequestedDay, RequestedPart) != (Day, Part) ? Name(RequestedDay, RequestedPart) : null;

    private string Name(int day, int part) => SlotsPerDay > 1 ? $"December {day}, {Year} (post {part})" : $"December {day}, {Year}";
}

/// <summary>Why a request wasn't approved, as the email to the contributor explains it.</summary>
public enum ClaimDeclineReason
{
    /// <summary>Their spot went to someone else, and other days are still open.</summary>
    SpotTaken,

    /// <summary>Their spot went to someone else, and every spot is taken now.</summary>
    SeasonFull,

    /// <summary>The organizers didn't approve it for another reason. They can reply to ask why.</summary>
    NotApproved,
}

/// <summary>The emails about claim requests. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class ClaimEmails
{
    public static EmailContent Received(ClaimEmailDetails claim, SiteOptions site) => new()
    {
        Kind = "claim-received",
        Subject = $"We received your C# Advent request for December {claim.Day}",
        Preview = "The organizers review every request. You'll hear from us once they've decided.",
        Heading = $"Thanks, {claim.DisplayName}!",
        Body =
        [
            Paragraph($"We received your request for {claim.Spot} in C# Advent {claim.Year}."),
            Paragraph(
                "The organizers review every request, and you'll get another email once they've decided. If that day goes to " +
                "someone else, we'll tell you, so you can pick another."),
            Paragraph("Changed your mind? Withdraw it from your ", Link("My spots", MySpots(site)), " page while it's waiting."),
            Paragraph("Questions? Just reply to this email."),
            SignOff,
        ],
        Reason = $"You're getting this email because you requested a spot in C# Advent {claim.Year}.",
        Transactional = true,
    };

    public static EmailContent NewRequest(ClaimEmailDetails claim, SiteOptions site) => new()
    {
        Kind = "claim-new-request",
        Subject = $"New C# Advent request: {claim.DisplayName} for December {claim.Day}",
        Preview = $"{claim.DisplayName} asked for {claim.Spot}.",
        Heading = "New claim request",
        Body =
        [
            Paragraph($"{claim.DisplayName} asked for {claim.Spot}."),
            Details(
                new DetailRow("Name", claim.DisplayName),
                new DetailRow("GitHub", claim.GithubLogin),
                new DetailRow("Email", claim.Email),
                new DetailRow("Link", claim.Link ?? "None"),
                new DetailRow("Comments", claim.Comments ?? "None")),
            Button("Review the request", $"{Site(site)}/manage/requests/{claim.RequestId}"),
            Paragraph("Replying to this email reaches the contributor."),
        ],
        Reason = "You're getting this email because you're a C# Advent organizer.",
    };

    public static EmailContent Approved(ClaimEmailDetails claim, SiteOptions site) => new()
    {
        Kind = claim.AskedFor is null ? "claim-approved" : "claim-approved-other-spot",
        Subject = $"You're on the C# Advent calendar: December {claim.Day}",
        Preview = "Your request is approved. Here's what happens next.",
        Heading = $"You're in, {claim.DisplayName}!",
        Body =
        [
            claim.AskedFor is { } asked
                ? Paragraph(
                    $"Your request is approved, for a different day than you asked for: {claim.Spot} is yours, instead of {asked}. " +
                    "If it doesn't work for you, reply to this email and we'll sort it out.")
                : Paragraph($"Your request is approved: {claim.Spot} is yours."),
            Paragraph("Here's what happens next:"),
            List(
                ["Prepare your post. It can be about anything related to C#, and it should be in English."],
                [
                    $"Open your post by saying it's part of the {claim.Year} C# Advent calendar, with a link to ", Link(Host(site), Site(site)),
                    ", so your readers can find the rest of the Advent. Any wording is fine; there's one ready to paste on your My spots page. " +
                    "We check every post, and one without it when it's announced goes to a backup poster.",
                ],
                [$"Keep it to yourself until December {claim.Day}, then publish it and share it with #csharp and #csadvent."],
                ["When you know your post's title and URL, sign in and add them on your ", Link("My spots", MySpots(site)), " page. That's how they get onto the calendar."]),
            Button("Add your post details", MySpots(site)),
            Paragraph(
                "Can't make it after all? Drop out from your ", Link("My spots", MySpots(site)),
                " page as soon as you can, and we'll offer your day to our backup posters."),
            Paragraph("The full rules are in our ", Link("terms", $"{Site(site)}/terms"), "."),
            SignOff,
        ],
        Reason = $"You're getting this email because you requested a spot in C# Advent {claim.Year}.",
        Transactional = true,
    };

    /// <summary>To the contributor, when an organizer declines their request.</summary>
    public static EmailContent Declined(ClaimEmailDetails claim, ClaimDeclineReason reason, SiteOptions site) => new()
    {
        Kind = reason switch { ClaimDeclineReason.SpotTaken => "claim-declined-taken", ClaimDeclineReason.SeasonFull => "claim-declined-full", _ => "claim-declined" },
        Subject = $"About your C# Advent request for December {claim.Day}",
        Preview = reason is ClaimDeclineReason.NotApproved
            ? "We couldn't approve your request this time."
            : $"December {claim.Day} has gone to another author.",
        Heading = $"Sorry, {claim.DisplayName}",
        Body = reason switch
        {
            ClaimDeclineReason.SpotTaken =>
            [
                Paragraph($"Thanks for asking for {claim.Spot} in C# Advent {claim.Year}. That spot has gone to another author, so we can't give it to you."),
                Paragraph("Plenty of other days are still open, though. Pick one on the calendar and request it, and we'll review it as soon as we can."),
                Button("See the open days", Site(site)),
                Paragraph("Questions? Just reply to this email."),
                SignOff,
            ],
            ClaimDeclineReason.SeasonFull =>
            [
                Paragraph(
                    $"Thanks for asking for {claim.Spot} in C# Advent {claim.Year}. That spot has gone to another author, and every " +
                    "spot on the calendar is taken now."),
                Paragraph(
                    "You can still join the backup list. When an author drops out, or their post doesn't arrive, we ask the backups " +
                    "one at a time, in the order they signed up."),
                Button("Join the backup list", $"{Site(site)}/backups/{claim.Year}"),
                Paragraph("Questions? Just reply to this email."),
                SignOff,
            ],
            _ =>
            [
                Paragraph($"Thanks for asking for {claim.Spot} in C# Advent {claim.Year}. We couldn't approve your request this time."),
                Paragraph("Questions? Just reply to this email."),
                SignOff,
            ],
        },
        Reason = $"You're getting this email because you requested a spot in C# Advent {claim.Year}.",
        Transactional = true,
    };

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers");

    private static string Site(SiteOptions site) => site.BaseUrl.TrimEnd('/');

    private static string Host(SiteOptions site) => new Uri(site.BaseUrl).Host;

    /// <summary>Where contributors add their post's title and URL, or drop out.</summary>
    public static string MySpots(SiteOptions site) => $"{Site(site)}/my-spots";
}
