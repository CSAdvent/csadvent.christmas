using System.Globalization;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Announcements;

/// <summary>The invitation past authors get when a season's sign-ups open (#133). Optional, so it has the unsubscribe link.</summary>
public static class PastAuthorEmails
{
    public static EmailContent Invitation(PastAuthor author, int year, SiteOptions site)
    {
        var root = site.BaseUrl.TrimEnd('/');
        var history = author.Posts switch
        {
            0 => "you've taken part in C# Advent before",
            1 => string.Create(CultureInfo.InvariantCulture, $"you wrote a post for C# Advent {author.FirstYear}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"you've written {author.Posts} posts for C# Advent since {author.FirstYear}"),
        };

        return new EmailContent
        {
            Kind = "past-author-invitation",
            Priority = EmailPriority.Invitation,
            Subject = SingleLine($"C# Advent {year} sign-ups are open: you wrote for us before"),
            Preview = "Claim a day in December and write for C# Advent again.",
            Heading = SingleLine($"Come back for C# Advent {year}, {author.Name}!"),
            Body =
            [
                Paragraph($"Sign-ups for C# Advent {year} are open, and since {history}, we'd love to have you back."),
                Paragraph(
                    "Pick a day in December, claim it, and write a post about C#. Two posts go up each day from December 1 to 25, " +
                    "and spots go quickly."),
                Button("Claim a day", $"{root}/?via=email"),
                Paragraph(
                    "Can't commit to a day? Join the ",
                    Link("backup list", string.Create(CultureInfo.InvariantCulture, $"{root}/backups/{year}")),
                    " and fill in if an author drops out."),
                Paragraph("Thanks,", EmailSpan.LineBreak, "Matt and Calvin, the C# Advent organizers"),
            ],
            Reason = "You're getting this email because you've written for C# Advent before.",
        };
    }
}
