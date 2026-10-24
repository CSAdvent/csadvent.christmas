namespace CSAdvent.Web.Entries;

/// <summary>Where a link to an entry came from, from its <c>?via=</c> tag.</summary>
public static class EntryVia
{
    /// <summary>Everywhere we put links: the platforms we post to, our own emails, feed and pages, and the retrospectives.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "bluesky", "mastodon", "facebook", "linkedin", "reddit", "rss", "email", "calendar", "site", "retrospective",
    };

    /// <summary>A known tag, <c>other</c> for anything else, or nothing when there's no tag.</summary>
    public static string? Normalize(string? via)
    {
        var tag = via?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(tag) ? null : Known.Contains(tag) ? tag : "other";
    }
}

/// <summary>Which requests count as a person visiting: not link-preview fetchers, crawlers, scripts or prefetches.</summary>
public static class EntryTraffic
{
    // Link previews (Facebook, LinkedIn, Mastodon, Bluesky, Slack, Discord, Teams...), search crawlers and scripts.
    private static readonly string[] AutomatedAgents =
    [
        "bot", "crawl", "spider", "slurp", "preview", "facebookexternalhit", "facebookcatalog", "embedly", "mastodon", "cardyb",
        "bluesky", "whatsapp", "skypeuripreview", "headless", "python", "curl", "wget", "go-http-client", "okhttp",
        "java/", "httpclient", "axios", "node-fetch",
    ];

    public static bool IsPerson(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        // Browsers that load a page ahead of a click say so.
        if (request.Headers["Sec-Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase)
            || request.Headers["Purpose"].ToString().Contains("prefetch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var agent = request.Headers.UserAgent.ToString();
        return agent.Length > 0 && !AutomatedAgents.Any(marker => agent.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
