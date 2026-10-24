using System.Globalization;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Entries;

namespace CSAdvent.Web.Social;

/// <summary>The author's accounts, from the claim form, for tagging them.</summary>
public sealed record AuthorAccounts(string? Bluesky, string? Mastodon, string? LinkedIn, string? Facebook, string? Reddit)
{
    public static readonly AuthorAccounts None = new(null, null, null, null, null);
}

/// <summary>What every platform's announcement of an entry says. Only the formatting differs (<see cref="AnnouncementFormatter"/>).</summary>
public sealed record Announcement(int Year, int Day, int Part, int SlotsPerDay, string Title, string Author, AuthorAccounts Accounts, string BaseUrl)
{
    /// <summary>What a test post says instead of the entry's day.</summary>
    public string? Label { get; init; }

    /// <summary>Where a test post links instead of an entry's page, relative to the base URL.</summary>
    public string? Path { get; init; }

    /// <summary>For example "C# Advent 2026, December 6, part 2".</summary>
    public string When => Label ?? (SlotsPerDay > 1 ? $"C# Advent {Year}, December {Day}, part {Part}" : $"C# Advent {Year}, December {Day}");

    /// <summary>The entry's page on our site, tagged with the platform, so the visit is counted and the click goes through us.</summary>
    public string Link(SocialPlatform platform)
    {
        var via = platform.ToString().ToLowerInvariant();
        return EntryLinks.Absolute(BaseUrl, Path is null ? EntryLinks.Page(Year, Day, Part, via) : $"{Path}?via={via}");
    }

    /// <summary>
    /// A test post: formatted exactly as an announcement is, but plainly a test, about no entry. It links to a page of its
    /// own (<c>/test-post/{id}</c>), so platforms fetch a fresh preview for it, as they will for a real entry's new page.
    /// </summary>
    public static Announcement Test(int year, string baseUrl, string id) =>
        new(year, 1, 1, 2, "Checking that C# Advent can post here automatically", "the C# Advent organizers", AuthorAccounts.None, baseUrl)
        {
            Label = "C# Advent test post (please ignore)",
            Path = $"test-post/{id}",
        };
}

/// <summary>An announcement ready for one platform.</summary>
/// <param name="Text">What to paste. For Reddit, the post's title (the link goes in its own field).</param>
/// <param name="Length">How the platform counts <paramref name="Text"/>.</param>
/// <param name="Limit">The platform's limit, if it has a practical one.</param>
/// <param name="ComposerUrl">A link that opens the platform's composer with the text filled in, where there is one.</param>
/// <param name="TagHint">Who to tag by hand, where pasted text can't do it (LinkedIn, Facebook, Reddit).</param>
public sealed record FormattedAnnouncement(
    SocialPlatform Platform,
    string Text,
    string Link,
    int Length,
    int? Limit,
    string? ComposerUrl,
    string? TagHint);

/// <summary>Formats an <see cref="Announcement"/> for each platform's limits and conventions.</summary>
public static partial class AnnouncementFormatter
{
    public const int BlueskyLimit = 300;
    public const int MastodonLimit = 500;
    public const int LinkedInLimit = 3000;
    public const int RedditTitleLimit = 300;

    private const string Hashtags = "#csharp #dotnet #csadvent";

    /// <param name="ours">C# Advent's accounts, for the Mastodon server and the subreddit the composer links open.</param>
    public static FormattedAnnouncement Format(Announcement announcement, SocialPlatform platform, SocialAccountsOptions? ours = null)
    {
        var link = announcement.Link(platform);
        var mastodonInstance = ours?.MastodonServer;
        var submit = ours?.Subreddit is { Length: > 0 } subreddit ? $"https://www.reddit.com/r/{subreddit}/submit" : "https://www.reddit.com/submit";
        return platform switch
        {
            SocialPlatform.Bluesky => Fit(announcement, BlueskyLimit, Graphemes, title =>
                Compose(announcement.When, title, announcement.Author, Mention(announcement.Accounts.Bluesky), link, Hashtags),
                text => (platform, link, null, $"https://bsky.app/intent/compose?text={Uri.EscapeDataString(text)}")),
            SocialPlatform.Mastodon => Fit(announcement, MastodonLimit, MastodonLength, title =>
                Compose(announcement.When, title, announcement.Author, announcement.Accounts.Mastodon, link, Hashtags),
                text => (platform, link, null, mastodonInstance is null ? null : $"https://{mastodonInstance}/share?text={Uri.EscapeDataString(text)}")),
            SocialPlatform.LinkedIn => Fit(announcement, LinkedInLimit, text => text.Length, title =>
                Compose(announcement.When, title, announcement.Author, null, link, Hashtags),
                _ => (platform, link, Hint(announcement.Accounts.LinkedIn), null)),
            SocialPlatform.Facebook => Fit(announcement, null, text => text.Length, title =>
                Compose(announcement.When, title, announcement.Author, null, link, "#csharp #csadvent"),
                _ => (platform, link, Hint(announcement.Accounts.Facebook), null)),
            _ => Fit(announcement, RedditTitleLimit, text => text.Length, title =>
                $"{Quote(title)} by {announcement.Author} ({announcement.When})",
                text => (platform, link, Hint(announcement.Accounts.Reddit is { } user ? $"u/{user}" : null),
                    $"{submit}?url={Uri.EscapeDataString(link)}&title={Uri.EscapeDataString(text)}")),
        };
    }

    /// <summary>Bluesky counts graphemes, so an emoji counts once.</summary>
    public static int Graphemes(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>Mastodon counts every link as 23 characters, and a mention without its server.</summary>
    public static int MastodonLength(string text) =>
        MentionDomain().Replace(Url().Replace(text, new string('x', 23)), "").Length;

    private static string Compose(string when, string title, string author, string? mention, string link, string hashtags) =>
        $"{when}: {Quote(title)} by {author}{(mention is null ? "" : $" ({mention})")}\n\n{link}\n\n{hashtags}";

    private static string Quote(string title) => $"“{title}”";

    private static string? Mention(string? blueskyHandle) => blueskyHandle is null ? null : $"@{blueskyHandle}";

    private static string? Hint(string? account) => account is null ? null : $"Tag the author: {account}";

    /// <summary>Builds the text, shortening the title with an ellipsis until it fits the limit.</summary>
    private static FormattedAnnouncement Fit(
        Announcement announcement,
        int? limit,
        Func<string, int> measure,
        Func<string, string> build,
        Func<string, (SocialPlatform Platform, string Link, string? Hint, string? Composer)> extras)
    {
        var title = announcement.Title;
        var text = build(title);
        while (limit is { } max && measure(text) > max && title.Length > 1)
        {
            title = title[..^2].TrimEnd() + "…";
            text = build(title);
        }

        var (platform, link, hint, composer) = extras(text);
        return new FormattedAnnouncement(platform, text, link, measure(text), limit, composer, hint);
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    // "@user@server" counts as "@user".
    [GeneratedRegex(@"(?<=@[A-Za-z0-9_]+)@[A-Za-z0-9.\-]+")]
    private static partial Regex MentionDomain();
}
