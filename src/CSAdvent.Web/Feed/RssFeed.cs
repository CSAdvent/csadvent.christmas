using System.Globalization;
using System.Text;
using System.Xml;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Retrospectives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Feed;

/// <summary>A revealed entry, or a season's published retrospective, as a feed item.</summary>
/// <param name="RevealedAt">When the entry was revealed, or the retrospective published: the item's date.</param>
public sealed record FeedItem(int Year, int Day, int Part, int SlotsPerDay, string Title, string Author, DateTimeOffset RevealedAt)
{
    /// <summary>A retrospective's page, relative to the base URL, instead of an entry's.</summary>
    public string? Path { get; init; }

    /// <summary>A retrospective's description, instead of the entry's <see cref="Summary"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The post in one line, if its author gave one (#208).</summary>
    public string? PostSummary { get; init; }

    /// <summary>
    /// For example "Day 6, post 2 of C# Advent 2025, by Ada Lovelace.", after the post's own summary if it has one: "Pattern
    /// matching, all the way down. Day 6, post 2 of C# Advent 2025, by Ada Lovelace."
    /// </summary>
    public string Summary
    {
        get
        {
            if (Description is not null)
            {
                return Description;
            }

            var line = SlotsPerDay > 1 ? $"Day {Day}, post {Part} of C# Advent {Year}, by {Author}." : $"Day {Day} of C# Advent {Year}, by {Author}.";
            return PostSummary is { } summary ? $"{PostSummaries.AsSentence(summary)} {line}" : line;
        }
    }

    /// <summary>Stable, so editing an item never makes it look new: <c>csadvent-2025-6-2</c>, or <c>csadvent-2025-retrospective</c>.</summary>
    public string Guid => Path is null
        ? string.Create(CultureInfo.InvariantCulture, $"csadvent-{Year}-{Day}-{Part}")
        : string.Create(CultureInfo.InvariantCulture, $"csadvent-{Year}-retrospective");

    public static FeedItem Retrospective(int year, string title, string description, DateTimeOffset publishedAt) =>
        new(year, 0, 0, 0, title, "The C# Advent organizers", publishedAt) { Path = RetrospectiveLinks.Page(year), Description = description };
}

/// <summary>
/// The RSS 2.0 feeds: the latest <see cref="LatestItems"/> revealed entries and published retrospectives from every season at
/// <c>/rss</c>, or everything from one season at <c>/archive/{year}/rss</c>.
/// Items are newest first (by reveal time, then post order within a day), dated with their reveal time, and link to the
/// entry's page on our site tagged <c>?via=rss</c>, never straight to the post. GUIDs come from the season, day and post,
/// so editing a title or URL never makes an item look new.
/// </summary>
public sealed class RssFeed(AdventDbContext db, AdventSchedule schedule, IOptions<SiteOptions> site)
{
    /// <summary>
    /// How many items <c>/rss</c> carries (#194): two seasons' worth, newest first, which is all a feed reader looks at. Every
    /// season's own feed keeps all of its posts.
    /// </summary>
    public const int LatestItems = 100;

    public async Task<List<FeedItem>> GetItemsAsync(int? year = null, CancellationToken cancellationToken = default)
    {
        var limit = year is null ? LatestItems : int.MaxValue;

        // Newest first, so the all-seasons feed stops reading once it has enough revealed posts.
        var slots = db.Slots
            .AsNoTracking()
            // Never the test season, even for its testers: feed readers remember items, and these are deleted afterwards.
            .Where(s => (year == null || s.SeasonYear == year) && s.SeasonYear != Season.TestYear && s.PostTitle != null && s.PostUrl != null && s.AuthorName != null)
            .OrderByDescending(s => s.SeasonYear).ThenByDescending(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new { s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.PostTitle, s.AuthorName, s.PostSummary })
            .AsAsyncEnumerable();

        var entries = new List<FeedItem>();
        await foreach (var s in slots.WithCancellation(cancellationToken))
        {
            // Only what's been revealed: the reveal time depends on the time zone, so it's checked here rather than in SQL (#5).
            if (!schedule.IsRevealed(s.SeasonYear, s.Day))
            {
                continue;
            }

            entries.Add(new FeedItem(s.SeasonYear, s.Day, s.Part, s.SlotsPerDay, s.PostTitle!, s.AuthorName!, schedule.GetRevealTime(s.SeasonYear, s.Day)) { PostSummary = s.PostSummary });
            if (entries.Count == limit)
            {
                break;
            }
        }

        var retrospectives = await db.Retrospectives
            .AsNoTracking()
            .Where(r => (year == null || r.SeasonYear == year) && r.PublishedAt != null)
            .ToListAsync(cancellationToken);

        return [.. entries
            .Concat(retrospectives.Select(r => FeedItem.Retrospective(r.SeasonYear, r.Title, RetrospectiveDraft.Summary(r.Stats), r.PublishedAt!.Value)))
            .OrderByDescending(item => item.RevealedAt)
            .ThenBy(item => item.Part)
            .Take(limit)];
    }

    public string Write(IReadOnlyList<FeedItem> items, int? year = null)
    {
        var root = site.Value.BaseUrl.TrimEnd('/');
        var self = year is { } season ? $"{root}/archive/{season}/rss" : $"{root}/rss";

        var output = new StringBuilder();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
        using (var writer = XmlWriter.Create(new Utf8StringWriter(output), settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("rss");
            writer.WriteAttributeString("version", "2.0");
            writer.WriteAttributeString("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
            writer.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");

            writer.WriteStartElement("channel");
            writer.WriteElementString("title", year is { } y ? $"C# Advent {y}" : "C# Advent");
            writer.WriteElementString("link", year is { } linkYear ? $"{root}/archive/{linkYear}" : root);
            writer.WriteElementString("description", "Two C# posts a day, December 1 to 25, written by the C# community.");
            writer.WriteElementString("language", "en-us");
            if (items.Count > 0)
            {
                writer.WriteElementString("lastBuildDate", Rfc822(items[0].RevealedAt));
            }

            writer.WriteStartElement("atom", "link", "http://www.w3.org/2005/Atom");
            writer.WriteAttributeString("href", self);
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("type", "application/rss+xml");
            writer.WriteEndElement();

            // RSS 2.0 allows a channel image up to 144 pixels wide.
            writer.WriteStartElement("image");
            writer.WriteElementString("url", $"{root}/{FeedImage}");
            writer.WriteElementString("title", year is { } imageYear ? $"C# Advent {imageYear}" : "C# Advent");
            writer.WriteElementString("link", root);
            writer.WriteElementString("width", "144");
            writer.WriteElementString("height", "144");
            writer.WriteEndElement();

            foreach (var item in items)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("link", EntryLinks.Absolute(root, item.Path is { } path ? $"{path}?via=rss" : EntryLinks.Page(item.Year, item.Day, item.Part, "rss")));
                writer.WriteElementString("description", item.Summary);
                writer.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", item.Author);
                writer.WriteElementString("pubDate", Rfc822(item.RevealedAt));
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermaLink", "false");
                writer.WriteString(item.Guid);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToString();
    }

    /// <summary>The logo at 144 pixels. A stable path, because feed readers keep it.</summary>
    public const string FeedImage = "images/csadvent-logo-144.png";

    private static string Rfc822(DateTimeOffset moment) => moment.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

    /// <summary>A StringWriter that says UTF-8, so the XML declaration does too.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
