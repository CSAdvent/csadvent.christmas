using System.Net;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Social.Posting;

namespace CSAdvent.Web.Blurbs;

/// <summary>
/// The rule (#128): every post opens by saying it's part of that year's C# Advent calendar, with a link back to the site. The
/// wording is the author's; <see cref="SuggestedBlurb"/> is only a ready-made example.
/// </summary>
public static class BlurbRules
{
    /// <summary>The first season with the rule.</summary>
    public const int FirstYear = 2026;

    /// <summary>The site the blurb links to, whatever site this is running on.</summary>
    public const string Host = "csadvent.christmas";

    public const string SiteUrl = "https://" + Host;

    /// <summary>Whether a season's posts need the blurb: 2026 on, and the test season, so it can be rehearsed.</summary>
    public static bool AppliesTo(int year) => year >= FirstYear;

    /// <summary>Whether a link goes to csadvent.christmas (any page, any subdomain).</summary>
    public static bool IsLinkBack(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http"
        && (uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith($".{Host}", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The deadline for a post's blurb: when it's announced on its day (<see cref="SocialPostingOptions.PostAt"/>), 10 AM
    /// Eastern for post 1 and 2 PM for post 2. A backup standing by takes the spot then.
    /// </summary>
    public static DateTimeOffset Deadline(AdventSchedule schedule, SocialPostingOptions posting, int year, int day, int part) =>
        schedule.FromLocalTime(schedule.GetDate(year, day).ToDateTime(posting.TimeFor(part)));
}

/// <summary>A blurb ready to paste at the top of a post, as plain text, Markdown and HTML.</summary>
/// <param name="Calendar">The words that link back: "2026 C# Advent calendar".</param>
/// <param name="Rest">What follows them.</param>
public sealed record SuggestedBlurb(string Calendar, string Rest)
{
    public static SuggestedBlurb For(int year, int slotsPerDay) =>
        new($"{year} C# Advent calendar", slotsPerDay > 1 ? "two C# posts a day, December 1–25." : "a C# post a day, December 1–25.");

    public string Text => $"This post is part of the {Calendar} ({BlurbRules.SiteUrl}): {Rest}";

    public string Markdown => $"This post is part of the [{Calendar}]({BlurbRules.SiteUrl}): {Rest}";

    public string Html => $"<p>This post is part of the <a href=\"{BlurbRules.SiteUrl}\">{WebUtility.HtmlEncode(Calendar)}</a>: {WebUtility.HtmlEncode(Rest)}</p>";
}
