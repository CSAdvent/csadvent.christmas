using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Reminders;

/// <summary>An author whose spot has no post link yet, some days before their day.</summary>
public sealed record PostLinkReminder(string AuthorName, int Year, int Day, int Part, int SlotsPerDay, int DaysBefore)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";
}

/// <summary>A revealed post, as the daily email lists it.</summary>
public sealed record SharedPost(string Title, string Url, string AuthorName);

/// <summary>The daily "today's posts" email for one author in the season.</summary>
public sealed record DailyShare(int Year, int Day, IReadOnlyList<SharedPost> Posts, string RecipientName, bool RecipientIsAuthorToday);

/// <summary>The reminder emails. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class ReminderEmails
{
    /// <summary>From this many days out, the reminder warns that a backup poster may take the spot.</summary>
    public const int BackupWarningDays = 2;

    public static EmailContent PostLinkMissing(PostLinkReminder reminder, SiteOptions site)
    {
        var when = reminder.DaysBefore == 1 ? "tomorrow" : $"in {reminder.DaysBefore} days";
        List<EmailBlock> body =
        [
            Paragraph($"Hi {reminder.AuthorName}, your C# Advent day is {reminder.Spot}, and we don't have the link to your post yet."),
            Paragraph(
                "Sign in and add your post's title and URL on your ", Link("My spots", ClaimEmails.MySpots(site)), " page. You can " +
                "add them before the post is public, as long as you don't publish it until your day."),
            Button("Add your post link", ClaimEmails.MySpots(site)),
        ];
        if (reminder.DaysBefore <= BackupWarningDays)
        {
            body.Add(Paragraph(
                "If we still don't have your link the evening before your day, we'll start lining up a backup poster. If your " +
                "link doesn't arrive before your day starts at midnight Eastern, the backup takes your spot."));
        }

        body.Add(Paragraph(
            "Can't make it after all? Drop out from your ", Link("My spots", ClaimEmails.MySpots(site)),
            " page as soon as you can, so a backup poster can take your day."));
        body.Add(SignOff);

        return new EmailContent
        {
            Subject = $"Your C# Advent post is due {when} (December {reminder.Day})",
            Preview = "We don't have the link to your post yet. Sign in and add it when it's ready.",
            Heading = reminder.DaysBefore == 1 ? "Your day is tomorrow!" : $"Your day is {reminder.DaysBefore} days away!",
            Body = body,
            Reason = $"You're getting this email because you have a spot in C# Advent {reminder.Year} and we don't have your post's link yet.",
        };
    }

    public static EmailContent Daily(DailyShare share, SiteOptions site, string unsubscribeUrl)
    {
        List<EmailBlock> body =
        [
            Paragraph($"Hi {share.RecipientName}! Here's what's new on the C# Advent calendar today:"),
            new ListBlock([.. share.Posts.Select(post => (IReadOnlyList<EmailSpan>)[Link(post.Title, post.Url), $" by {post.AuthorName}"])]),
        ];
        if (share.RecipientIsAuthorToday)
        {
            body.Add(Paragraph("One of today's posts is yours. Thank you for taking part!"));
        }

        body.Add(Paragraph(
            "Help the authors out: share today's posts with your friends, coworkers and followers, with #csharp and #csadvent."));
        body.Add(Button("See the whole calendar", site.BaseUrl.TrimEnd('/')));
        body.Add(SignOff);

        return new EmailContent
        {
            Subject = share.Posts.Count == 1
                ? $"Day {share.Day} of C# Advent: {share.Posts[0].Title}"
                : $"Day {share.Day} of C# Advent: {share.Posts.Count} new posts to share",
            Preview = "Today's posts are live. Help spread the word!",
            Heading = $"December {share.Day}: today's posts are live",
            Body = body,
            Reason = $"You're getting this because you have a spot in C# Advent {share.Year}. We send it each day of Advent.",
            UnsubscribeUrl = unsubscribeUrl,
        };
    }

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Calvin and Matt, the C# Advent organizers");
}
