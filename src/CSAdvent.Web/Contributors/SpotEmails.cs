using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Contributors;

/// <summary>An author giving up their spot.</summary>
public sealed record DropoutDetails(string AuthorName, int Year, int Day, int Part, int SlotsPerDay)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";
}

/// <summary>An author changing their post's title or summary on My spots after it was revealed (#155): each as it was and is now.</summary>
public sealed record PostChangeDetails(
    int SlotId,
    string AuthorName,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string? OldTitle,
    string? NewTitle,
    string? OldSummary,
    string? NewSummary)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} (post {Part})" : $"December {Day}, {Year}";

    public bool TitleChanged => OldTitle != NewTitle;

    public bool SummaryChanged => OldSummary != NewSummary;
}

/// <summary>Emails about contributors' own spots. <see cref="EmailCatalog"/> lists when each one is sent.</summary>
public static class SpotEmails
{
    public static EmailContent DroppedOut(DropoutDetails dropout, SiteOptions site) => new()
    {
        Kind = "dropped-out",
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

    /// <summary>
    /// To the organizers, when an author changes their post's title or summary after its reveal (#155). The change shows
    /// straight away, so this says what it was, in case it needs putting back. A new link waits for them instead.
    /// </summary>
    public static EmailContent PostChanged(PostChangeDetails change, SiteOptions site)
    {
        var what = (change.TitleChanged, change.SummaryChanged) switch
        {
            (true, true) => "title and summary",
            (true, false) => "title",
            _ => "summary",
        };

        return new()
        {
            Kind = "post-changed",
            Subject = SingleLine($"{change.AuthorName} changed the {what} of their post for December {change.Day}"),
            Preview = SingleLine($"Their post for {change.Spot} was already revealed, so the change is live."),
            Heading = "An author changed a revealed post",
            Body =
            [
                Paragraph($"{change.AuthorName} changed the {what} of their post for {change.Spot} on My spots, after it was revealed:"),
                List([.. Changes(change)]),
                Paragraph(
                    "It's on the calendar and the post's page already, and in any announcement that hasn't gone out yet. If it isn't " +
                    "right, change it back in the entry editor."),
                Button("Open the entry editor", $"{site.BaseUrl.TrimEnd('/')}/manage/entries/{change.SlotId}"),
            ],
            Reason = "You're getting this email because you're a C# Advent organizer.",
        };
    }

    private static IEnumerable<EmailSpan[]> Changes(PostChangeDetails change)
    {
        if (change.TitleChanged)
        {
            yield return ["Title", EmailSpan.LineBreak, $"Was: {change.OldTitle ?? "none"}", EmailSpan.LineBreak, $"Now: {change.NewTitle ?? "none"}"];
        }

        if (change.SummaryChanged)
        {
            yield return ["Summary", EmailSpan.LineBreak, $"Was: {change.OldSummary ?? "none"}", EmailSpan.LineBreak, $"Now: {change.NewSummary ?? "none"}"];
        }
    }
}
