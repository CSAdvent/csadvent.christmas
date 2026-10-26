using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social;

namespace CSAdvent.Web.Content;

/// <summary>
/// schema.org structured data for search engines (#195), rendered as JSON-LD by <c>JsonLd</c>: the site on the home page,
/// and each season as an event. Entry pages have none: search engines go straight to the author's post (decided 2026-10-26),
/// and the link previews that still get the page read its Open Graph tags.
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
