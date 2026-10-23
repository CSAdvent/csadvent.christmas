using System.Globalization;
using System.Text;
using System.Xml;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Feed;

/// <summary>A revealed entry, as a feed item.</summary>
public sealed record FeedItem(int Year, int Day, int Part, int SlotsPerDay, string Title, string Author, DateTimeOffset RevealedAt)
{
    /// <summary>For example "Day 6, post 2 of C# Advent 2025, by Ada Lovelace."</summary>
    public string Summary => SlotsPerDay > 1
        ? $"Day {Day}, post {Part} of C# Advent {Year}, by {Author}."
        : $"Day {Day} of C# Advent {Year}, by {Author}.";
}

/// <summary>
/// The RSS 2.0 feeds: every revealed entry from every season at <c>/rss</c>, or one season at <c>/archive/{year}/rss</c>.
/// Items are newest first (by reveal time, then post order within a day), dated with their reveal time, and link to the
/// entry's page on our site tagged <c>?via=rss</c>, never straight to the post. GUIDs come from the season, day and post,
/// so editing a title or URL never makes an item look new.
/// </summary>
public sealed class RssFeed(AdventDbContext db, AdventSchedule schedule, IOptions<SiteOptions> site)
{
    public async Task<List<FeedItem>> GetItemsAsync(int? year = null, CancellationToken cancellationToken = default)
    {
        var slots = await db.Slots
            .AsNoTracking()
            // Never the test season, even for its testers: feed readers remember items, and these are deleted afterwards.
            .Where(s => (year == null || s.SeasonYear == year) && s.SeasonYear != Season.TestYear && s.PostTitle != null && s.PostUrl != null && s.AuthorName != null)
            .Select(s => new { s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.PostTitle, s.AuthorName })
            .ToListAsync(cancellationToken);

        // Only what's been revealed: the reveal time depends on the time zone, so it's checked here rather than in SQL (#5).
        return [.. slots
            .Where(s => schedule.IsRevealed(s.SeasonYear, s.Day))
            .Select(s => new FeedItem(s.SeasonYear, s.Day, s.Part, s.SlotsPerDay, s.PostTitle!, s.AuthorName!, schedule.GetRevealTime(s.SeasonYear, s.Day)))
            .OrderByDescending(item => item.RevealedAt)
            .ThenBy(item => item.Part)];
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

            writer.WriteStartElement("image");
            writer.WriteElementString("url", $"{root}/images/csadvent-logo.png");
            writer.WriteElementString("title", year is { } imageYear ? $"C# Advent {imageYear}" : "C# Advent");
            writer.WriteElementString("link", root);
            writer.WriteEndElement();

            foreach (var item in items)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("link", EntryLinks.Absolute(root, EntryLinks.Page(item.Year, item.Day, item.Part, "rss")));
                writer.WriteElementString("description", item.Summary);
                writer.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", item.Author);
                writer.WriteElementString("pubDate", Rfc822(item.RevealedAt));
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermaLink", "false");
                writer.WriteString(string.Create(CultureInfo.InvariantCulture, $"csadvent-{item.Year}-{item.Day}-{item.Part}"));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return output.ToString();
    }

    private static string Rfc822(DateTimeOffset moment) => moment.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

    /// <summary>A StringWriter that says UTF-8, so the XML declaration does too.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
