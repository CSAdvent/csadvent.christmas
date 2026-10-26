using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Social;

namespace CSAdvent.Web.Content;

/// <summary>
/// schema.org structured data for search engines (#195), rendered as JSON-LD by <c>JsonLd</c>: the site on the home page,
/// each season as an event, and each revealed entry as a blog post whose original is the author's own post. Nothing about a
/// post before its reveal (#5): an entry only gets one once its page shows the post.
/// </summary>
public static class StructuredData
{
    // The default encoder escapes <, > and &, so the JSON can't close its script element.
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.Default };

    public const string Description = "Two C# posts a day, December 1 to 25, written by the C# community.";

    /// <summary>The site, published by C# Advent, with its accounts elsewhere.</summary>
    public static JsonObject WebSite(string baseUrl, SocialAccountsOptions accounts) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebSite",
        ["name"] = "C# Advent",
        ["url"] = $"{Root(baseUrl)}/",
        ["description"] = Description,
        ["inLanguage"] = "en",
        ["publisher"] = Publisher(baseUrl, accounts),
    };

    /// <summary>
    /// A season as an online event, from its first reveal to the end of December 25. Never the test season, whose dates
    /// follow a rehearsal.
    /// </summary>
    /// <param name="page">The season's calendar, relative to the base URL: the home page until it's over, then the archive.</param>
    /// <param name="image">Its card, relative to the base URL.</param>
    public static JsonObject SeasonEvent(int year, AdventSchedule schedule, string baseUrl, string page, string image)
    {
        var url = $"{Root(baseUrl)}/{page}";
        return new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Event",
            ["name"] = $"C# Advent {year}",
            ["description"] = Description,
            ["url"] = url,
            ["startDate"] = Iso(schedule.GetRevealTime(year, 1)),
            ["endDate"] = Iso(schedule.ToLocalTime(schedule.GetDayEndTime(year, Season.DaysInAdvent))),
            ["eventStatus"] = "https://schema.org/EventScheduled",
            ["eventAttendanceMode"] = "https://schema.org/OnlineEventAttendanceMode",
            ["location"] = new JsonObject { ["@type"] = "VirtualLocation", ["url"] = url },
            ["image"] = new JsonArray($"{Root(baseUrl)}/{image}"),
            ["isAccessibleForFree"] = true,
            ["organizer"] = Organization(baseUrl),
        };
    }

    /// <summary>
    /// A revealed entry: the author's post (its <c>url</c>, the original), described by our page about it, published as part
    /// of C# Advent when it was revealed.
    /// </summary>
    /// <param name="image">The entry's card, relative to the base URL.</param>
    public static JsonObject Entry(EntryView entry, string baseUrl, string description, string image)
    {
        if (entry is not { IsRevealed: true, PostTitle: { } title, PostUrl: { } postUrl, AuthorName: { } author })
        {
            throw new ArgumentException("Only a revealed entry with a post has structured data (#5).", nameof(entry));
        }

        var root = Root(baseUrl);
        var person = new JsonObject { ["@type"] = "Person", ["name"] = author };
        if (entry.AuthorUrl is { } authorUrl)
        {
            person["url"] = authorUrl;
        }

        return new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = title,
            ["description"] = description,
            ["url"] = postUrl,
            ["mainEntityOfPage"] = EntryLinks.Absolute(root, EntryLinks.Page(entry.Year, entry.Day, entry.Part)),
            ["datePublished"] = Iso(entry.RevealsAt),
            ["author"] = person,
            ["image"] = $"{root}/{image}",
            ["inLanguage"] = "en",
            ["isPartOf"] = new JsonObject
            {
                ["@type"] = "CreativeWorkSeries",
                ["name"] = $"C# Advent {entry.Year}",
                ["url"] = entry.IsSeasonOver ? $"{root}/archive/{entry.Year}" : $"{root}/",
            },
            ["publisher"] = Organization(baseUrl),
        };
    }

    public static string Serialize(JsonObject data) => data.ToJsonString(Options);

    /// <summary>For <c>article:published_time</c> and JSON-LD dates: ISO 8601, with the offset.</summary>
    public static string Iso(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static JsonObject Organization(string baseUrl) => new()
    {
        ["@type"] = "Organization",
        ["name"] = "C# Advent",
        ["url"] = $"{Root(baseUrl)}/",
        ["logo"] = new JsonObject { ["@type"] = "ImageObject", ["url"] = $"{Root(baseUrl)}/images/csadvent-logo.png" },
    };

    private static JsonObject Publisher(string baseUrl, SocialAccountsOptions accounts)
    {
        var publisher = Organization(baseUrl);
        if (accounts.Profiles().Select(profile => (JsonNode?)profile.Url).ToArray() is { Length: > 0 } profiles)
        {
            publisher["sameAs"] = new JsonArray(profiles);
        }

        return publisher;
    }

    private static string Root(string baseUrl) => baseUrl.TrimEnd('/');
}
