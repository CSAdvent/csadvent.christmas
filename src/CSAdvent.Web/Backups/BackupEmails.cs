using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Backups;

/// <summary>Why a spot needs a backup poster.</summary>
public enum BackupReason
{
    /// <summary>The author gave the spot up (or an organizer released it): it's open now.</summary>
    DroppedOut,

    /// <summary>The evening before the day, the author still hasn't sent their post's link. They can still, until the reveal.</summary>
    LinkMissing,

    /// <summary>
    /// On the day, the post doesn't open with the C# Advent blurb (#128). The author can still add it until it's announced, or
    /// an organizer asked for a backup because they won't.
    /// </summary>
    BlurbMissing,
}

/// <summary>A spot, as the backup emails describe it.</summary>
public sealed record BackupSpot(int Year, int Day, int Part, int SlotsPerDay)
{
    public string Name => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";
}

/// <summary>The backup poster emails. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class BackupEmails
{
    private const string ForBackups = "You're getting this email because you're on the C# Advent backup list.";
    private const string ForOrganizers = "You're getting this email because you're a C# Advent organizer.";

    public static EmailContent SignedUp(string name, int year, int position, SiteOptions site) => new()
    {
        Subject = $"You're on the C# Advent {year} backup list",
        Preview = $"You're number {position} on the list. We'll email you if a spot needs filling.",
        Heading = $"Thanks for being a backup, {name}!",
        Body =
        [
            Paragraph($"You're number {position} on the C# Advent {year} backup list."),
            Paragraph(
                "If an author drops out, or hasn't sent their post's link by the evening before their day, we ask the backups one " +
                "at a time, in the order they signed up. Each person has 4 hours to answer before we ask the next."),
            Paragraph(
                "It helps to have a post ready to go, because notice can be short. If you can't step in when we ask, just say so: " +
                "you stay on the list for the next one."),
            Button("See your place on the list", $"{Site(site)}/backups/{year}"),
            SignOff,
        ],
        Reason = ForBackups,
        Transactional = true,
    };

    /// <param name="answerBy">When the next backup gets asked, already in the reveal time zone, such as "10:00 PM Eastern on December 4".</param>
    /// <param name="deadline">For a missing blurb: when the author's time runs out, such as "10 AM Eastern".</param>
    public static EmailContent Ask(string name, BackupSpot spot, BackupReason reason, string answerBy, string offerUrl, SiteOptions site, string? deadline = null)
    {
        List<EmailBlock> body = reason switch
        {
            BackupReason.DroppedOut =>
            [
                Paragraph($"Hi {name}, the author of {spot.Name} had to drop out, so the spot is open. Do you have a post you'd like to use?"),
            ],
            BackupReason.BlurbMissing =>
            [
                Paragraph(
                    $"Hi {name}, the post for {spot.Name} doesn't open with the C# Advent blurb, which every post needs. Do you " +
                    "have a post you'd like to use in its place?"),
                Paragraph(
                    "It has to be published already, and open by saying it's part of the ", Link($"{spot.Year} C# Advent calendar", "https://csadvent.christmas"),
                    ", with a link to csadvent.christmas. Any wording is fine. We check it when you send it."),
                Paragraph(
                    $"There's no guarantee yet: the original author can still add the blurb until {deadline ?? "their post is announced"}. " +
                    "If they do, we'll let you know, and you stay on the list for the next one. If they don't, your post takes the spot then."),
            ],
            _ =>
            [
                Paragraph($"Hi {name}, we still don't have the post for {spot.Name}. Do you have a post you'd like to use in its place?"),
                Paragraph(
                    $"There's no guarantee yet: the original author can still send their link until their day starts at midnight " +
                    $"Eastern. If they do, we'll let you know, and you stay on the list for the next one. If they don't, your post " +
                    $"goes on the calendar instead."),
            ],
        };

        body.Add(Button($"Fill in on December {spot.Day}", offerUrl));
        body.Add(Paragraph(
            $"If you can't this time, there's a button for that on the same page, and you stay on the list. If we haven't heard " +
            $"from you by {answerBy}, we'll ask the next backup too, but you can still take the " +
            $"spot as long as nobody else has."));
        body.Add(SignOff);

        return new EmailContent
        {
            Subject = $"Can you fill in on C# Advent, December {spot.Day}?",
            Preview = reason switch
            {
                BackupReason.DroppedOut => $"{spot.Name} is open. You're next on the backup list.",
                BackupReason.BlurbMissing => $"{spot.Name} might need a post today. You're next on the backup list.",
                _ => $"{spot.Name} might need a post. You're next on the backup list.",
            },
            Heading = $"Can you fill in on December {spot.Day}?",
            Body = body,
            Reason = ForBackups,
            Transactional = true,
        };
    }

    public static EmailContent YoureOn(string name, BackupSpot spot, string postTitle, SiteOptions site) => new()
    {
        Subject = $"Your post is on the C# Advent calendar: December {spot.Day}",
        Preview = "Thanks for stepping in!",
        Heading = $"Thank you, {name}!",
        Body =
        [
            Paragraph($"\"{postTitle}\" is now {spot.Name} on the C# Advent calendar."),
            Paragraph("Share it with #csharp and #csadvent when the day comes. You can still fix its title or URL on your My spots page."),
            Button("Open My spots", $"{Site(site)}/my-spots"),
            SignOff,
        ],
        Reason = ForBackups,
        Transactional = true,
    };

    public static EmailContent NotNeeded(string name, BackupSpot spot, SiteOptions site) => new()
    {
        Subject = $"You're off the hook for C# Advent, December {spot.Day}",
        Preview = "The original author came through. Thanks for being ready!",
        Heading = "The original author came through",
        Body =
        [
            Paragraph($"Hi {name}, the author of {spot.Name} came through after all, so we won't need yours this time."),
            Paragraph("Thanks for being ready to step in. You're still on the backup list for the next one."),
            SignOff,
        ],
        Reason = ForBackups,
        Transactional = true,
    };

    public static EmailContent SpotFilled(string backupName, BackupSpot spot, BackupReason reason, SiteOptions site) => new()
    {
        Subject = $"A backup poster filled C# Advent, December {spot.Day}",
        Preview = $"{backupName} stepped in for {spot.Name}.",
        Heading = "A backup poster stepped in",
        Body =
        [
            Paragraph(reason switch
            {
                BackupReason.DroppedOut => $"{backupName} filled {spot.Name}, which its author had dropped.",
                BackupReason.BlurbMissing => $"{backupName} filled {spot.Name}, because its post didn't open with the C# Advent blurb.",
                _ => $"{backupName} filled {spot.Name}, because its author's link didn't arrive before the reveal.",
            }),
            Button("See the season's entries", $"{Site(site)}/manage/entries?year={spot.Year}"),
        ],
        Reason = ForOrganizers,
    };

    public static EmailContent NoBackups(BackupSpot spot, SiteOptions site) => new()
    {
        Subject = $"No backup poster for C# Advent, December {spot.Day}",
        Preview = $"Every backup has been asked about {spot.Name}, and nobody could take it.",
        Heading = "We ran out of backups",
        Body =
        [
            Paragraph($"Every backup poster has been asked about {spot.Name}, and nobody has taken it. It needs an organizer's help."),
            Paragraph("If someone joins the backup list while the spot still needs a post, we'll ask them too."),
            Button("See the season's entries", $"{Site(site)}/manage/entries?year={spot.Year}"),
        ],
        Reason = ForOrganizers,
    };

    public static EmailContent Replaced(string authorName, BackupSpot spot, SiteOptions site, BackupReason reason = BackupReason.LinkMissing) => new()
    {
        Subject = $"Your C# Advent spot went to a backup poster",
        Preview = reason is BackupReason.BlurbMissing
            ? $"Your post for {spot.Name} didn't open with the C# Advent blurb in time."
            : $"We didn't get your post's link for {spot.Name} in time.",
        Heading = $"Sorry we missed you, {authorName}",
        Body =
        [
            Paragraph(reason is BackupReason.BlurbMissing
                ? $"Your post for {spot.Name} didn't open with the C# Advent blurb by the time it was due to be announced, so a " +
                    "backup poster's post went on the calendar instead."
                : $"We didn't get your post's link for {spot.Name} before your day started, so a backup poster's post went on the " +
                    "calendar instead."),
            Paragraph("No hard feelings: things happen. We hope you'll join us again next year."),
            SignOff,
        ],
        Reason = $"You're getting this email because you had a spot in C# Advent {spot.Year}.",
        Transactional = true,
    };

    private static ParagraphBlock SignOff => Paragraph("Thanks,", EmailSpan.LineBreak, "Calvin and Matt, the C# Advent organizers");

    private static string Site(SiteOptions site) => site.BaseUrl.TrimEnd('/');
}
