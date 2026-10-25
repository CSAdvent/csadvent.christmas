using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace CSAdvent.Web.Claims;

public enum SocialPlatform
{
    Bluesky,
    Mastodon,
    LinkedIn,
    Facebook,
    Reddit,
    X,
}

/// <summary>
/// Contributors' social accounts, so posts about their entries can tag them. Each platform accepts what people usually
/// paste (a handle with or without "@", or a profile URL) and stores one normal form.
/// </summary>
public static partial class SocialAccounts
{
    /// <summary>
    /// The normal form of <paramref name="input"/>, or null if it's empty. Returns false if it isn't a valid account.
    /// </summary>
    public static bool TryNormalize(SocialPlatform platform, string? input, out string? normalized)
    {
        normalized = null;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        normalized = platform switch
        {
            SocialPlatform.Bluesky => Match(BlueskyPattern(), text, m => m.Groups["handle"].Value.ToLowerInvariant()),
            SocialPlatform.Mastodon => Match(MastodonHandlePattern(), text, m => $"@{m.Groups["user"].Value}@{m.Groups["host"].Value.ToLowerInvariant()}")
                ?? Match(MastodonUrlPattern(), text, m => $"@{m.Groups["user"].Value}@{m.Groups["host"].Value.ToLowerInvariant()}"),
            SocialPlatform.LinkedIn => Match(LinkedInPattern(), text, m => $"https://www.linkedin.com/in/{m.Groups["slug"].Value}"),
            SocialPlatform.Facebook => Match(FacebookPattern(), text, m => $"https://www.facebook.com/{m.Groups["path"].Value}"),
            SocialPlatform.Reddit => Match(RedditPattern(), text, m => m.Groups["user"].Value),
            SocialPlatform.X => Match(XPattern(), text, m => m.Groups["user"].Value),
            _ => null,
        };

        return normalized is not null;
    }

    /// <summary>Where an account's profile is on the web, from its normal form (<see cref="TryNormalize"/>).</summary>
    public static string ProfileUrl(SocialPlatform platform, string normalized) => platform switch
    {
        SocialPlatform.Bluesky => $"https://bsky.app/profile/{normalized}",
        SocialPlatform.Mastodon when normalized.Split('@', StringSplitOptions.RemoveEmptyEntries) is [var user, var host] => $"https://{host}/@{user}",
        SocialPlatform.Reddit => $"https://www.reddit.com/user/{normalized}",
        SocialPlatform.X => $"https://x.com/{normalized}",
        _ => normalized,
    };

    /// <summary>
    /// One of the social links on a GitHub profile as an account we tag, if it's on one of our platforms. GitHub names the
    /// Mastodon links it recognizes; the rest are known by their address, because GitHub calls some of them (Bluesky, for
    /// one) "generic". Anything else, such as a LinkedIn company page, is skipped.
    /// </summary>
    public static (SocialPlatform Platform, string Account)? FromGitHub(string provider, string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        SocialPlatform? platform = provider.ToLowerInvariant() switch
        {
            "mastodon" or "hometown" => SocialPlatform.Mastodon,
            _ when IsHostOrSubdomain(host, "bsky.app") => SocialPlatform.Bluesky,
            _ when IsHostOrSubdomain(host, "linkedin.com") => SocialPlatform.LinkedIn,
            _ when IsHostOrSubdomain(host, "facebook.com") || IsHostOrSubdomain(host, "fb.com") => SocialPlatform.Facebook,
            _ when IsHostOrSubdomain(host, "reddit.com") => SocialPlatform.Reddit,
            _ when IsHostOrSubdomain(host, "x.com") || IsHostOrSubdomain(host, "twitter.com") => SocialPlatform.X,
            _ => null,
        };

        return platform is { } found && TryNormalize(found, url, out var account) && account is not null ? (found, account) : null;
    }

    /// <summary>
    /// Whether a link is a social media profile rather than someone's own site. The link for an author's name is for their
    /// website or blog, because their social accounts have fields of their own.
    /// </summary>
    public static bool IsSocialProfile(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (SocialHosts.Any(social => IsHostOrSubdomain(host, social)))
        {
            return true;
        }

        // Mastodon profiles are https://{any server}/@user. Blogs and channels on these sites look the same.
        return MastodonProfilePath().IsMatch(uri.AbsolutePath) && !NotMastodon.Any(site => IsHostOrSubdomain(host, site));
    }

    private static readonly string[] SocialHosts =
    [
        "bsky.app", "bsky.social", "twitter.com", "x.com", "linkedin.com", "facebook.com", "fb.com", "reddit.com",
        "instagram.com", "threads.net", "threads.com", "tiktok.com",
    ];

    private static readonly string[] NotMastodon = ["medium.com", "substack.com", "youtube.com"];

    private static bool IsHostOrSubdomain(string host, string domain) =>
        host == domain || host.EndsWith($".{domain}", StringComparison.Ordinal);

    private static string? Match(Regex pattern, string text, Func<Match, string> format) =>
        pattern.Match(text) is { Success: true } match ? format(match) : null;

    [GeneratedRegex(@"^/@[a-z0-9_]{1,30}/?$", RegexOptions.IgnoreCase)]
    private static partial Regex MastodonProfilePath();

    // "alice.bsky.social", "@alice.bsky.social", "https://bsky.app/profile/alice.bsky.social"
    [GeneratedRegex(@"^(?:https?://bsky\.app/profile/|@)?(?<handle>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9])/?$", RegexOptions.IgnoreCase)]
    private static partial Regex BlueskyPattern();

    // "@alice@hachyderm.io", "alice@hachyderm.io"
    [GeneratedRegex(@"^@?(?<user>[a-z0-9_]{1,30})@(?<host>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,})$", RegexOptions.IgnoreCase)]
    private static partial Regex MastodonHandlePattern();

    // "https://hachyderm.io/@alice"
    [GeneratedRegex(@"^https?://(?<host>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,})/@(?<user>[a-z0-9_]{1,30})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex MastodonUrlPattern();

    // "https://www.linkedin.com/in/alice", "linkedin.com/in/alice/", "alice"
    [GeneratedRegex(@"^(?:(?:https?://)?(?:[a-z]{2,3}\.)?linkedin\.com/in/)?(?<slug>[a-z0-9][a-z0-9_-]{2,99})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedInPattern();

    // "https://www.facebook.com/alice.dev", "facebook.com/alice.dev", "fb.com/alice.dev", "alice.dev",
    // "https://www.facebook.com/profile.php?id=1000123"
    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.|m\.|web\.)?(?:facebook|fb)\.com/)?(?<path>profile\.php\?id=\d{5,20}|[a-z0-9.]{5,50})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex FacebookPattern();

    // "alice", "u/alice", "/u/alice", "https://www.reddit.com/user/alice"
    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.|old\.)?reddit\.com/(?:u|user)/|/?u/)?(?<user>[a-z0-9_-]{3,20})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex RedditPattern();

    // "alice", "@alice", "https://x.com/alice", "twitter.com/alice", "https://x.com/alice?s=21" (the app's share link)
    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.|mobile\.)?(?:x|twitter)\.com/|@)?(?<user>[a-z0-9_]{1,15})/?(?:\?[^\s/]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex XPattern();
}

/// <summary>A link that isn't a social media profile (<see cref="SocialAccounts.IsSocialProfile"/>). Empty is allowed.</summary>
public sealed class NotSocialProfileAttribute : ValidationAttribute
{
    public const string Message = "That's a social media profile. Add it under Social accounts: this link is for your website or blog.";

    public NotSocialProfileAttribute() => ErrorMessage = Message;

    public override bool IsValid(object? value) => !SocialAccounts.IsSocialProfile(value as string);
}

/// <summary>A valid account on <paramref name="platform"/> in any form <see cref="SocialAccounts"/> accepts. Empty is allowed.</summary>
public sealed class SocialAccountAttribute(SocialPlatform platform) : ValidationAttribute
{
    public SocialPlatform Platform { get; } = platform;

    public override bool IsValid(object? value) => SocialAccounts.TryNormalize(Platform, value as string, out _);
}
