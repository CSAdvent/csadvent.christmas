namespace CSAdvent.Web.Entries;

/// <summary>Where a link to an entry came from, from its <c>?via=</c> tag.</summary>
public static class EntryVia
{
    /// <summary>Everywhere we put links: the platforms we post to, our own emails, feed and pages, and the retrospectives.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "bluesky", "mastodon", "facebook", "linkedin", "reddit", "x", "rss", "email", "calendar", "site", "retrospective",
    };

    /// <summary>A known tag, <c>other</c> for anything else, or nothing when there's no tag.</summary>
    public static string? Normalize(string? via)
    {
        var tag = via?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(tag) ? null : Known.Contains(tag) ? tag : "other";
    }
}

/// <summary>
/// Which requests count as a person visiting: not link-preview fetchers, crawlers, scripts or prefetches. And which are link
/// previews, the only visitors (with the organizers) who get an entry's page rather than going straight to the post.
/// </summary>
public static class EntryTraffic
{
    // Link previews (Facebook, LinkedIn, Mastodon, Bluesky, X's Twitterbot, Slack, Discord, Teams...), search crawlers and scripts.
    private static readonly string[] AutomatedAgents =
    [
        "bot", "crawl", "spider", "slurp", "preview", "facebookexternalhit", "facebookcatalog", "embedly", "mastodon", "cardyb",
        "bluesky", "whatsapp", "skypeuripreview", "headless", "python", "curl", "wget", "go-http-client", "okhttp",
        "java/", "httpclient", "axios", "node-fetch",
    ];

    /// <summary>
    /// The link previews that read an entry's page for its card and Open Graph tags (decided 2026-10-26): Facebook, X,
    /// LinkedIn, Mastodon, Bluesky, Slack, Discord, Telegram, WhatsApp, Reddit, Iframely, Embedly, and Teams and Skype. Everyone
    /// else, search crawlers and unknown bots included, goes straight to the post, so crawlers never see what readers don't.
    /// None of them is ever counted.
    /// </summary>
    public static readonly IReadOnlyList<string> LinkPreviewAgents =
    [
        "facebookexternalhit", "facebot", "twitterbot", "linkedinbot", "mastodon", "cardyb", "slackbot-linkexpanding", "discordbot",
        "telegrambot", "whatsapp", "redditbot", "iframely", "embedly", "skypeuripreview",
    ];

    /// <summary>A link preview fetching the page for its card (<see cref="LinkPreviewAgents"/>).</summary>
    public static bool IsLinkPreview(HttpRequest request) => Matches(request.Headers.UserAgent.ToString(), LinkPreviewAgents);

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
        return agent.Length > 0 && !Matches(agent, AutomatedAgents) && !Matches(agent, LinkPreviewAgents);
    }

    /// <summary>
    /// A person following a <c>/go</c> link or an entry page's (#150). Blazor's enhanced navigation (blazor.web.js, which the
    /// site no longer loads, but a page open since before may still have) fetches the link first, can't follow the redirect to
    /// another site, and then loads it again as a whole page. Only that second request counts.
    /// </summary>
    public static bool IsClick(HttpRequest request) =>
        IsPerson(request) && !request.Headers.Accept.ToString().Contains("blazor-enhanced-nav=on", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string agent, IEnumerable<string> markers) =>
        markers.Any(marker => agent.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
