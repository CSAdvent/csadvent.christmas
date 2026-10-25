using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Contributors;

/// <summary>What the emails about a request to change older posts' links say.</summary>
/// <param name="Login">The requester's GitHub login.</param>
/// <param name="Name">Their name, from their profile or GitHub, if they have one.</param>
public sealed record PostLinkEmailDetails(int RequestId, string Login, string? Name, string? Note, IReadOnlyList<PostLinkEmailChange> Changes)
{
    /// <summary>"Ada Lovelace (ada-lovelace)", or just the login.</summary>
    public string Who => Name is { Length: > 0 } name && !string.Equals(name, Login, StringComparison.OrdinalIgnoreCase) ? $"{name} ({Login})" : Login;

    /// <summary>"1 post", "3 posts".</summary>
    public string Posts => Changes.Count == 1 ? "1 post" : $"{Changes.Count} posts";
}

/// <summary>One post's link, before and after.</summary>
public sealed record PostLinkEmailChange(int Year, int Day, int Part, int SlotsPerDay, string Title, string OldUrl, string NewUrl)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year}, post {Part}" : $"December {Day}, {Year}";
}

/// <summary>Emails about changing the links of someone's older posts.</summary>
public static class PostLinkEmails
{
    /// <summary>To the organizers, for every request. Replies go to the requester.</summary>
    public static EmailContent NewRequest(PostLinkEmailDetails request, SiteOptions site) => new()
    {
        Subject = SingleLine($"New post links? {request.Login} asked to change the links to {request.Posts}"),
        Preview = SingleLine($"{request.Who} asked for new links to {request.Posts} from earlier seasons of C# Advent."),
        Heading = "Someone wants new links to their posts",
        Body =
        [
            Paragraph($"{request.Who} asked to change the links to {request.Posts} from earlier seasons of C# Advent:"),
            List([.. request.Changes.Select(change => new EmailSpan[]
            {
                $"“{change.Title}” ({change.Spot})", EmailSpan.LineBreak,
                $"From: {change.OldUrl}", EmailSpan.LineBreak,
                $"To: {change.NewUrl}",
            })]),
            Details(
                new DetailRow("Asked by", $"{request.Who}, https://github.com/{request.Login}"),
                new DetailRow("Their note", request.Note ?? "None")),
            Paragraph("Check that each new link opens the same post before approving. Approving changes them all."),
            Button("Review the request", $"{Site(site)}/{PostLinkLinks.Review(request.RequestId)}"),
            Paragraph("Replying to this email reaches them."),
        ],
        Reason = "You're getting this email because you're a C# Advent organizer.",
    };

    /// <summary>To the requester, once it's approved.</summary>
    public static EmailContent Approved(PostLinkEmailDetails request, SiteOptions site) => new()
    {
        Subject = "Your C# Advent posts' links are updated",
        Preview = SingleLine($"We've changed the links to {request.Posts}."),
        Heading = "Your links are updated",
        Body =
        [
            Paragraph($"We've changed the links to {request.Posts}:"),
            List([.. request.Changes.Select(change => new EmailSpan[]
            {
                Link(change.Title, EntryLinks.Absolute(Site(site), EntryLinks.Page(change.Year, change.Day, change.Part))),
                $" ({change.Spot}) now goes to {change.NewUrl}",
            })]),
            Paragraph("The archive and your posts' pages send readers to the new addresses from now on."),
            Button("See your history", ClaimEmails.MySpots(site)),
            SignOff,
        ],
        Reason = "You're getting this email because you asked to change the links to your C# Advent posts.",
        Transactional = true,
    };

    /// <summary>To the requester, if it's declined. Replies go to the organizers.</summary>
    public static EmailContent Declined(PostLinkEmailDetails request, SiteOptions site) => new()
    {
        Subject = "About your C# Advent posts' links",
        Preview = SingleLine($"We didn't change the links to {request.Posts}."),
        Heading = "We didn't change them",
        Body =
        [
            Paragraph($"We couldn't confirm the new links you asked for, so the links to {request.Posts} are as they were:"),
            List([.. request.Changes.Select(change => new EmailSpan[] { $"“{change.Title}” ({change.Spot}): {change.NewUrl}" })]),
            Paragraph(
                "Usually that's because a new link didn't open the same post. Reply to this email if we've got it wrong, or ask again " +
                "from ", Link("My spots", ClaimEmails.MySpots(site)), "."),
            SignOff,
        ],
        Reason = "You're getting this email because you asked to change the links to your C# Advent posts.",
        Transactional = true,
    };

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers");

    private static string Site(SiteOptions site) => site.BaseUrl.TrimEnd('/');
}
