using System.Globalization;
using System.Text;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;

namespace CSAdvent.Web.Retrospectives;

/// <summary>
/// Writes a retrospective's first draft from the season's numbers, in Markdown. It's a complete post, but only a starting point:
/// the organizers rewrite it as they like before publishing, and nothing regenerates it behind their back. A season without
/// visit or click data (every season before 2026) gets only what's known.
/// </summary>
public static class RetrospectiveDraft
{
    /// <summary>The <c>?via=</c> tag on links from a retrospective to its posts.</summary>
    public const string Via = "retrospective";

    public static string Title(int year) => string.Create(CultureInfo.InvariantCulture, $"C# Advent {year}: a look back");

    /// <summary>For link previews and the RSS feed: "A look back at C# Advent 2026: 50 posts by 48 authors."</summary>
    public static string Summary(RetrospectiveStats stats) =>
        $"A look back at C# Advent {stats.Year}: {Count(stats.Posts, "post")} by {Count(stats.Authors, "author")}"
        + (stats.HasTraffic ? $", and {Count(stats.Clicks, "click")} through to them." : ".");

    public static string Body(RetrospectiveStats stats)
    {
        var text = new StringBuilder();
        void Paragraph(string line) => text.Append(line).Append("\n\n");
        void Heading(string heading) => Paragraph($"## {heading}");

        Paragraph($"C# Advent {stats.Year} is a wrap! Thank you to the {Count(stats.Authors, "author")} who wrote this season's {Count(stats.Posts, "post")}, and to everyone who read them.");

        Heading("The season");
        var season = new List<string>
        {
            $"**{Count(stats.Posts, "post")}** by **{Count(stats.Authors, "author")}**, {(stats.SlotsPerDay > 1 ? "two" : "one")} a day from December 1 to 25.",
        };
        if (stats.Year > Season.FirstYear && stats.Authors > 0)
        {
            season.Add($"**{Count(stats.FirstTimeAuthors, "first-time author")}** and **{Number(stats.ReturningAuthors)} returning**, who'd written for C# Advent before.");
        }

        if (stats.AuthorsBySeasonsWritten.LastOrDefault() is { Seasons: >= 3 } veterans)
        {
            season.Add(veterans.Authors == 1
                ? $"One of this season's authors has now written for C# Advent in {Number(veterans.Seasons)} different seasons."
                : $"{Number(veterans.Authors)} of this season's authors have now written for C# Advent in {Number(veterans.Seasons)} different seasons.");
        }

        if (stats.BackupsSteppedIn > 0)
        {
            season.Add($"Backup posters stepped in **{Count(stats.BackupsSteppedIn, "time")}** when an author couldn't make it.");
        }

        if (stats.Requests > 0)
        {
            season.Add($"{Count(stats.Requests, "request")} for spots, {Number(stats.RequestsFirstDay)} of them in the first 24 hours after sign-ups opened.");
        }

        Paragraph(string.Join('\n', season.Select(line => $"- {line}")));

        if (stats.HasTraffic)
        {
            Heading("Readers");
            Paragraph(stats.Views > 0
                ? $"Readers clicked through to the posts **{Count(stats.Clicks, "time")}** in December, and opened their pages on our site {Count(stats.Views, "time")}."
                : $"Readers clicked through to the posts **{Count(stats.Clicks, "time")}** in December.");

            if (stats.ClicksPerDay.Count > 0 && stats.ClicksPerDay.Max() is var busiest and > 0)
            {
                var day = stats.ClicksPerDay.ToList().IndexOf(busiest) + 1;
                Paragraph($"The busiest day was December {day}, with {Count(busiest, "click")}.");
            }

            if (stats.ClicksBySource.Count > 0)
            {
                Paragraph($"Most clicks came from {List([.. stats.ClicksBySource.Take(3).Select(s => SourceName(s.Source))])}.");
            }
        }

        if (stats.MostRead.Count > 0)
        {
            Heading("The most-read posts");
            Paragraph(string.Join('\n', stats.MostRead.Select((entry, index) =>
                string.Create(CultureInfo.InvariantCulture, $"{index + 1}. [{Escape(entry.Title)}]({EntryLinks.Page(stats.Year, entry.Day, entry.Part, Via)}) by {Escape(entry.Author)} ({Count(entry.Clicks, "click")})"))));
        }

        if (stats.SocialPosts.Count > 0)
        {
            Heading("On social media");
            Paragraph($"We announced the posts on social media: {List([.. stats.SocialPosts.Select(p => $"{Number(p.Posts)} on {p.Platform}")])}.");
        }

        Heading($"Since {Season.FirstYear}");
        Paragraph(stats.Year == Season.FirstYear || stats.History.Count <= 1
            ? "This was the very first C# Advent."
            : $"That brings C# Advent to **{Count(stats.AllTimePosts, "post")}** by **{Count(stats.AllTimeAuthors, "different author")}** since the first season in {Season.FirstYear}.");

        text.Append("See you next December!\n");
        return text.ToString();
    }

    /// <summary>
    /// Escapes the characters Markdown would act on in a post's title or an author's name, such as <c>*</c>, <c>_</c>, brackets
    /// and backticks (C# titles often have <c>Span&lt;T&gt;</c> or <c>`code`</c>), so they show as typed.
    /// </summary>
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '~' or '|' or '&' or '#' or '!')
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    private static string SourceName(string source) => source switch
    {
        "calendar" => "the calendar",
        "email" => "the daily email",
        "rss" => "the RSS feed",
        RetrospectiveCalculator.Direct => "links people shared",
        "other" => "elsewhere",
        _ => new SourceClicks(source, 0).Label,
    };

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Count(int value, string noun) => $"{Number(value)} {noun}{(value == 1 ? "" : "s")}";

    /// <summary>"a", "a and b", "a, b and c".</summary>
    private static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };
}
