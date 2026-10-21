using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Contributors;

/// <summary>An author giving up their spot.</summary>
public sealed record DropoutDetails(string AuthorName, int Year, int Day, int Part, int SlotsPerDay)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";
}

/// <summary>Emails about contributors' own spots. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class SpotEmails
{
    public static EmailContent DroppedOut(DropoutDetails dropout, SiteOptions site) => new()
    {
        Subject = $"{dropout.AuthorName} dropped out of C# Advent, December {dropout.Day}",
        Preview = $"{dropout.Spot} is open again, and the backup posters are being asked.",
        Heading = "An author dropped out",
        Body =
        [
            Paragraph($"{dropout.AuthorName} gave up {dropout.Spot} from their My spots page."),
            Paragraph(
                "The spot is open again and they won't be named anywhere. We're asking the backup posters to fill it, one at a " +
                "time in sign-up order, and we'll email you if nobody can."),
            Button("See the season's entries", $"{site.BaseUrl.TrimEnd('/')}/manage/entries?year={dropout.Year}"),
        ],
        Reason = "You're getting this email because you're a C# Advent organizer.",
    };
}
