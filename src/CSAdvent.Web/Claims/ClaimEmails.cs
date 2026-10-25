using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Claims;

/// <summary>A claim request, as the emails about it need it.</summary>
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
    int SlotsPerDay)
{
    /// <summary>The spot, such as "December 3, 2026 (post 1)". The post number only matters when a day has more than one.</summary>
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";
}

/// <summary>The emails about claim requests. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class ClaimEmails
{
    public static EmailContent Received(ClaimEmailDetails claim, SiteOptions site) => new()
    {
        Subject = $"We received your C# Advent request for December {claim.Day}",
        Preview = "The organizers review every request. You'll hear from us when yours is approved.",
        Heading = $"Thanks, {claim.DisplayName}!",
        Body =
        [
            Paragraph($"We received your request for {claim.Spot} in C# Advent {claim.Year}."),
            Paragraph(
                "The organizers review every request, and you'll get another email when yours is approved. If that day is " +
                "taken by then, we'll get in touch about another one."),
            Paragraph("Questions? Just reply to this email."),
            SignOff,
        ],
        Reason = $"You're getting this email because you requested a spot in C# Advent {claim.Year}.",
        Transactional = true,
    };

    public static EmailContent NewRequest(ClaimEmailDetails claim, SiteOptions site) => new()
    {
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
        Subject = $"You're on the C# Advent calendar: December {claim.Day}",
        Preview = "Your request is approved. Here's what happens next.",
        Heading = $"You're in, {claim.DisplayName}!",
        Body =
        [
            Paragraph($"Your request is approved: {claim.Spot} is yours."),
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

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers");

    private static string Site(SiteOptions site) => site.BaseUrl.TrimEnd('/');

    private static string Host(SiteOptions site) => new Uri(site.BaseUrl).Host;

    /// <summary>Where contributors add their post's title and URL, or drop out.</summary>
    public static string MySpots(SiteOptions site) => $"{Site(site)}/my-spots";
}
