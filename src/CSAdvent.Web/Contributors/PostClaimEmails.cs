using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Contributors;

/// <summary>What the emails about a request to add an older post to someone's account say.</summary>
/// <param name="Login">The requester's GitHub login.</param>
/// <param name="Name">Their name, from their profile or GitHub, if they have one.</param>
/// <param name="Author">The author's name as it's shown on the post.</param>
public sealed record PostClaimEmailDetails(int RequestId, string Login, string? Name, string? Note, int Year, int Day, int Part, int SlotsPerDay, string Title, string Author)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year}, post {Part}" : $"December {Day}, {Year}";

    /// <summary>"Ada Lovelace (ada-lovelace)", or just the login.</summary>
    public string Who => Name is { Length: > 0 } name && !string.Equals(name, Login, StringComparison.OrdinalIgnoreCase) ? $"{name} ({Login})" : Login;
}

/// <summary>Emails about adding an older post to someone's account (#120).</summary>
public static class PostClaimEmails
{
    /// <summary>To the organizers, for every request. Replies go to the requester.</summary>
    public static EmailContent NewRequest(PostClaimEmailDetails request, SiteOptions site) => new()
    {
        Subject = SingleLine($"Is this their post? {request.Login} asked for a C# Advent {request.Year} post"),
        Preview = SingleLine($"{request.Who} asked to add “{request.Title}” to their account."),
        Heading = "Someone wants a post on their account",
        Body =
        [
            Paragraph($"{request.Who} asked to add a post from C# Advent {request.Year} to their account."),
            Details(
                new DetailRow("Post", request.Title),
                new DetailRow("Author on the post", request.Author),
                new DetailRow("When", request.Spot),
                new DetailRow("Asked by", $"{request.Who}, https://github.com/{request.Login}"),
                new DetailRow("Their note", request.Note ?? "None")),
            Paragraph("Check it's really theirs before approving, for example from the post's own site or their GitHub profile."),
            Button("Review the request", $"{Site(site)}/manage/authors/requests/{request.RequestId}"),
            Paragraph("Replying to this email reaches them."),
        ],
        Reason = "You're getting this email because you're a C# Advent organizer.",
    };

    /// <summary>To the requester, once it's approved.</summary>
    public static EmailContent Approved(PostClaimEmailDetails request, SiteOptions site) => new()
    {
        Subject = SingleLine($"Your C# Advent {request.Year} post is on your account"),
        Preview = SingleLine($"“{request.Title}” is now part of your C# Advent history."),
        Heading = "It's on your account",
        Body =
        [
            Paragraph(
                "“", Link(request.Title, EntryLinks.Absolute(Site(site), EntryLinks.Page(request.Year, request.Day, request.Part))),
                $"” from {request.Spot} is now linked to your GitHub account."),
            Paragraph(
                "It's in your C# Advent history on ", Link("My spots", ClaimEmails.MySpots(site)),
                ", and the post links to your website, or to your GitHub profile if you haven't given one. Your name and links are on ",
                Link("your profile", $"{Site(site)}/profile"), "."),
            Paragraph("Wrote for other seasons too? Find those posts on My spots and ask for them the same way."),
            Button("See your history", ClaimEmails.MySpots(site)),
            SignOff,
        ],
        Reason = "You're getting this email because you asked to add a C# Advent post to your account.",
        Transactional = true,
    };

    /// <summary>To the requester, if it's declined. Replies go to the organizers.</summary>
    public static EmailContent Declined(PostClaimEmailDetails request, SiteOptions site) => new()
    {
        Subject = SingleLine($"About your C# Advent {request.Year} post"),
        Preview = SingleLine($"We couldn't add “{request.Title}” to your account."),
        Heading = "We couldn't add it",
        Body =
        [
            Paragraph($"We couldn't confirm that “{request.Title}” from {request.Spot} is yours, so it isn't on your account."),
            Paragraph(
                "If it is yours, reply to this email with anything that shows it, such as the blog it was published on, and we'll take " +
                "another look."),
            SignOff,
        ],
        Reason = "You're getting this email because you asked to add a C# Advent post to your account.",
        Transactional = true,
    };

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers");

    private static string Site(SiteOptions site) => site.BaseUrl.TrimEnd('/');
}
