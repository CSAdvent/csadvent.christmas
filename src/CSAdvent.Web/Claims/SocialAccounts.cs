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
            _ => null,
        };

        return normalized is not null;
    }

    private static string? Match(Regex pattern, string text, Func<Match, string> format) =>
        pattern.Match(text) is { Success: true } match ? format(match) : null;

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
}

/// <summary>A valid account on <paramref name="platform"/> in any form <see cref="SocialAccounts"/> accepts. Empty is allowed.</summary>
public sealed class SocialAccountAttribute(SocialPlatform platform) : ValidationAttribute
{
    public SocialPlatform Platform { get; } = platform;

    public override bool IsValid(object? value) => SocialAccounts.TryNormalize(Platform, value as string, out _);
}
