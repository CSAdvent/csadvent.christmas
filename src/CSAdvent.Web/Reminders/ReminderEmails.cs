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

/// <summary>An author whose spot still has no post link on their day, a few hours before it's announced.</summary>
public sealed record DayOfReminder(string AuthorName, int Year, int Day, int Part, int SlotsPerDay, TimeOnly AnnouncedAt)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";

    /// <summary>"10 AM", or "2:30 PM".</summary>
    public string Announced => AnnouncedAt.ToString(AnnouncedAt.Minute == 0 ? "h tt" : "h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
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
            Transactional = true,
        };
    }

    /// <summary>The last call: it's the author's day, and their post is announced in a few hours.</summary>
    public static EmailContent PostLinkDueToday(DayOfReminder reminder, SiteOptions site) => new()
    {
        Subject = $"Today's your C# Advent day: add your post's link by {reminder.Announced} Eastern",
        Preview = "We still don't have the link to your post, and it's announced today.",
        Heading = "Today's your day!",
        Body =
        [
            Paragraph($"Hi {reminder.AuthorName}, today is your C# Advent day, {reminder.Spot}, and we still don't have the link to your post."),
            Paragraph(
                $"We announce your post on C# Advent's social media accounts at {reminder.Announced} Eastern, so please add its title " +
                "and URL on your ", Link("My spots", ClaimEmails.MySpots(site)), " page before then."),
            Button("Add your post link", ClaimEmails.MySpots(site)),
            Paragraph(
                "Until your link arrives, a backup poster can take your spot. If your post won't be ready today, drop out from your ",
                Link("My spots", ClaimEmails.MySpots(site)), " page so a backup can take your day."),
            SignOff,
        ],
        Reason = $"You're getting this email because you have a spot in C# Advent {reminder.Year} and we don't have your post's link yet.",
        Transactional = true,
    };

    public static EmailContent Daily(DailyShare share, SiteOptions site, string unsubscribeUrl) => DailyEmail(
        share.Day,
        share.Posts,
        $"Hi {share.RecipientName}!",
        share.RecipientIsAuthorToday,
        site,
        $"You're getting this because you have a spot in C# Advent {share.Year}. We send it each day of Advent.",
        unsubscribeUrl);

    /// <summary>
    /// The same email for readers who asked for each day's posts (#206), with the link that turns off only the daily posts.
    /// </summary>
    public static EmailContent DailyForReaders(int day, IReadOnlyList<SharedPost> posts, SiteOptions site, string unsubscribeUrl) => DailyEmail(
        day,
        posts,
        "Hi!",
        recipientIsAuthorToday: false,
        site,
        "You're getting this because you asked for C# Advent's daily posts by email. We send it each day of Advent.",
        unsubscribeUrl);

    private static EmailContent DailyEmail(
        int day,
        IReadOnlyList<SharedPost> posts,
        string greeting,
        bool recipientIsAuthorToday,
        SiteOptions site,
        string reason,
        string unsubscribeUrl)
    {
        List<EmailBlock> body =
        [
            Paragraph($"{greeting} Here's what's new on the C# Advent calendar today:"),
            new ListBlock([.. posts.Select(post => (IReadOnlyList<EmailSpan>)[Link(post.Title, post.Url), $" by {post.AuthorName}"])]),
        ];
        if (recipientIsAuthorToday)
        {
            body.Add(Paragraph("One of today's posts is yours. Thank you for taking part!"));
        }

        body.Add(Paragraph(
            "Help the authors out: share today's posts with your friends, coworkers and followers, with #csharp and #csadvent."));
        body.Add(Button("See the whole calendar", site.BaseUrl.TrimEnd('/')));
        body.Add(SignOff);

        return new EmailContent
        {
            Subject = posts.Count == 1
                ? $"Day {day} of C# Advent: {posts[0].Title}"
                : $"Day {day} of C# Advent: {posts.Count} new posts to share",
            Preview = "Today's posts are live. Help spread the word!",
            Heading = $"December {day}: today's posts are live",
            Body = body,
            Reason = reason,
            UnsubscribeUrl = unsubscribeUrl,
        };
    }

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers");
}
