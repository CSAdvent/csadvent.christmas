using CSAdvent.Web.Backups;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Blurbs;

/// <summary>A post, as the blurb emails describe it.</summary>
/// <param name="Deadline">When the blurb is due, already in the reveal time zone: "10:00 AM Eastern on December 6".</param>
public sealed record BlurbPost(int SlotId, BackupSpot Spot, string? Title, string Url, string Deadline);

/// <summary>The blurb check's emails (#128). <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class BlurbEmails
{
    private const string ForOrganizers = "You're getting this email because you're a C# Advent organizer.";

    /// <summary>To the author, once each time their post is found without the blurb.</summary>
    /// <param name="whatsWrong">What the check found, such as "There's no link to csadvent.christmas near the top of the post."</param>
    public static EmailContent Missing(string authorName, BlurbPost post, string whatsWrong, SiteOptions site)
    {
        var blurb = SuggestedBlurb.For(post.Spot.Year, post.Spot.SlotsPerDay);
        return new EmailContent
        {
            Subject = $"Please add the C# Advent blurb to your post for December {post.Spot.Day}",
            Preview = $"Your post doesn't open with it yet. Add it by {post.Deadline}.",
            Heading = "One thing before your post is announced",
            Body =
            [
                Paragraph($"Hi {authorName}, thanks for your post for {post.Spot.Name}! We check every post for the C# Advent blurb, and yours doesn't have it yet. {whatsWrong}"),
                Paragraph(
                    $"Please add a sentence at the top of your post saying it's part of the {post.Spot.Year} C# Advent calendar, with a " +
                    "link to csadvent.christmas. Any wording is fine. For example:"),
                Paragraph("This post is part of the ", Link(blurb.Calendar, BlurbRules.SiteUrl), $": {blurb.Rest}"),
                Paragraph("There's a copy ready to paste, in HTML and Markdown, on your My spots page."),
                Button("Open My spots", ClaimEmails.MySpots(site)),
                Paragraph(
                    $"Please add it by {post.Deadline}, when your post is announced. If it isn't there by then, a backup poster will " +
                    "take your spot. If you'd rather not add it, reply to this email and let us know."),
                Paragraph("We check again every few minutes, so there's no need to reply once it's there."),
                SignOff,
            ],
            Reason = $"You're getting this email because you have a spot in C# Advent {post.Spot.Year}.",
            Transactional = true,
        };
    }

    /// <summary>To the organizers, when the models can't settle a post, or it couldn't be checked, after its reveal.</summary>
    public static EmailContent NeedsDecision(BlurbPost post, BlurbStatus status, string detail, SiteOptions site) => new()
    {
        Subject = status is BlurbStatus.CouldNotCheck
            ? $"C# Advent, December {post.Spot.Day}: a post couldn't be checked for the blurb"
            : $"C# Advent, December {post.Spot.Day}: a post's blurb needs your decision",
        Preview = detail,
        Heading = status is BlurbStatus.CouldNotCheck ? "A post couldn't be checked" : "A post needs your decision",
        Body =
        [
            Paragraph(
                $"{post.Spot.Name}: ", Link(post.Title ?? post.Url, post.Url), "."),
            Paragraph(detail),
            Paragraph(
                $"It isn't announced automatically until it passes or you decide. Open it, look at the post, and mark it as passed or " +
                $"missing. Its deadline is {post.Deadline}."),
            Button("Decide", Entry(post, site)),
        ],
        Reason = ForOrganizers,
    };

    /// <summary>To the organizers, when a post's deadline passes without the blurb and no backup has taken the spot.</summary>
    public static EmailContent DeadlinePassed(BlurbPost post, SiteOptions site) => new()
    {
        Subject = $"C# Advent, December {post.Spot.Day}: no blurb and no backup",
        Preview = $"The post for {post.Spot.Name} still doesn't open with the blurb, and no backup has taken the spot.",
        Heading = "No blurb, and no backup yet",
        Body =
        [
            Paragraph(
                $"The post for {post.Spot.Name} (", Link(post.Title ?? post.Url, post.Url), ") still doesn't open with the C# Advent " +
                "blurb, and no backup poster has taken the spot."),
            Paragraph(
                "It stays on the calendar, but it isn't announced automatically. Backup posters are still being asked until the day " +
                "is over, and one who accepts takes the spot straight away. Or announce it anyway."),
            Button("Decide", Entry(post, site)),
        ],
        Reason = ForOrganizers,
    };

    private static string Entry(BlurbPost post, SiteOptions site) => $"{site.BaseUrl.TrimEnd('/')}/manage/entries/{post.SlotId}";

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Calvin and Matt, the C# Advent organizers");
}
