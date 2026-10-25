using System.Text.Json;
using System.Text.Json.Serialization;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Retrospectives;

/// <summary>
/// A season's numbers for its retrospective. They're taken when the draft is generated and kept with it, so the charts always
/// agree with the text written from them, however long after the season it's read. Only totals: nothing here says who read
/// what. Visits and clicks are counted from the first reveal to the end of December.
/// </summary>
/// <param name="Authors">Different authors this season.</param>
/// <param name="FirstTimeAuthors">This season's authors who'd never written for C# Advent before.</param>
/// <param name="AuthorsBySeasonsWritten">This season's authors by how many seasons they've written for, this one included.</param>
/// <param name="BackupsSteppedIn">Spots a backup poster took (only known until the season's contact details are deleted).</param>
/// <param name="Requests">Requests for spots (likewise).</param>
/// <param name="RequestsFirstDay">Requests in the first 24 hours after sign-ups opened.</param>
/// <param name="Views">Visits to the season's entry pages.</param>
/// <param name="Clicks">Clicks through to the season's posts, from the calendar or an entry page.</param>
/// <param name="ClicksPerDay">Clicks on each day of December: December 1 first. Empty without traffic.</param>
/// <param name="ClicksBySource">Clicks by where the link came from (<c>?via=</c>), most first.</param>
/// <param name="MostRead">The ten most-clicked posts.</param>
/// <param name="SocialPosts">The season's entries announced on each platform.</param>
/// <param name="History">Every finished season up to this one, oldest first.</param>
public sealed record RetrospectiveStats(
    int Year,
    int SlotsPerDay,
    int Spots,
    int Posts,
    int Authors,
    int FirstTimeAuthors,
    IReadOnlyList<SeasonsWritten> AuthorsBySeasonsWritten,
    int BackupsSteppedIn,
    int Requests,
    int RequestsFirstDay,
    int Views,
    int Clicks,
    IReadOnlyList<int> ClicksPerDay,
    IReadOnlyList<SourceClicks> ClicksBySource,
    IReadOnlyList<MostReadEntry> MostRead,
    IReadOnlyList<PlatformPosts> SocialPosts,
    IReadOnlyList<SeasonHistory> History)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [JsonIgnore]
    public int ReturningAuthors => Authors - FirstTimeAuthors;

    /// <summary>Whether any visits or clicks were counted. Seasons before 2026 have none.</summary>
    [JsonIgnore]
    public bool HasTraffic => Views + Clicks > 0;

    [JsonIgnore]
    public int AllTimePosts => History.Sum(h => h.Posts);

    /// <summary>Different authors since the first season: each is first-time once.</summary>
    [JsonIgnore]
    public int AllTimeAuthors => History.Sum(h => h.FirstTimeAuthors);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RetrospectiveStats FromJson(string json) =>
        JsonSerializer.Deserialize<RetrospectiveStats>(json, JsonOptions) ?? throw new JsonException("The retrospective's numbers are empty.");
}

/// <param name="Seasons">How many seasons, this one included.</param>
public sealed record SeasonsWritten(int Seasons, int Authors);

/// <param name="Source">A <c>?via=</c> tag, or <see cref="RetrospectiveCalculator.Direct"/> for links without one.</param>
public sealed record SourceClicks(string Source, int Clicks)
{
    /// <summary>What readers call it: "the calendar", "Bluesky", "the daily email".</summary>
    public string Label => Source switch
    {
        "calendar" => "The calendar",
        "email" => "The daily email",
        "rss" => "RSS",
        "bluesky" => "Bluesky",
        "mastodon" => "Mastodon",
        "facebook" => "Facebook",
        "linkedin" => "LinkedIn",
        "reddit" => "Reddit",
        "x" => "X",
        "retrospective" => "Retrospectives",
        RetrospectiveCalculator.Direct => "Shared and direct links",
        _ => "Elsewhere",
    };
}

public sealed record MostReadEntry(int Day, int Part, string Title, string Author, int Clicks);

public sealed record PlatformPosts(SocialPlatform Platform, int Posts);

/// <param name="Authors">Different authors that season.</param>
/// <param name="FirstTimeAuthors">That season's authors who'd never written for C# Advent before.</param>
public sealed record SeasonHistory(int Year, int Posts, int Authors, int FirstTimeAuthors)
{
    [JsonIgnore]
    public int ReturningAuthors => Authors - FirstTimeAuthors;
}
