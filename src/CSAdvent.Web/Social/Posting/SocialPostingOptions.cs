using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Social.Posting;

/// <summary>Automatic posting (#61): when announcements go out, and each platform's credentials (secrets, never in source).</summary>
public sealed class SocialPostingOptions
{
    public const string SectionName = "SocialPosting";

    /// <summary>
    /// When each entry's announcement goes out on its day, in the reveal time zone: post 1 at the first time, post 2 at the
    /// second. Not at the midnight reveal, when nobody's reading.
    /// </summary>
    [MinLength(1, ErrorMessage = "SocialPosting:PostAt needs at least one time.")]
    public TimeOnly[] PostAt { get; set; } = [];

    /// <summary>
    /// When the daily check of the Automatic platforms' credentials runs, in the reveal time zone
    /// (<see cref="SocialCredentialCheck"/>), early enough for an organizer to fix a failed one before the day's posts.
    /// </summary>
    public TimeOnly CheckAt { get; set; } = new(8, 0);

    /// <summary>How many times an announcement is tried before it waits for an organizer to retry it.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Pretend to post on the platforms without credentials, logging the announcement instead, so automatic posting can be
    /// tried in development and the tests without ever reaching a real account.
    /// </summary>
    public bool DryRun { get; set; }

    public MastodonCredentials Mastodon { get; set; } = new();

    public BlueskyCredentials Bluesky { get; set; } = new();

    public FacebookCredentials Facebook { get; set; } = new();

    /// <summary>When a part's announcement goes out. A part without a time of its own goes out at the last one.</summary>
    public TimeOnly TimeFor(int part) => PostAt[Math.Clamp(part, 1, PostAt.Length) - 1];
}

/// <summary>An access token for C# Advent's Mastodon account (<see cref="SocialAccountsOptions.Mastodon"/>), with <c>write:statuses</c>.</summary>
public sealed class MastodonCredentials
{
    public string? AccessToken { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>An app password for C# Advent's Bluesky account (<see cref="SocialAccountsOptions.Bluesky"/>).</summary>
public sealed class BlueskyCredentials
{
    public string? AppPassword { get; set; }

    /// <summary>The account's PDS, where it signs in and posts.</summary>
    public string Service { get; set; } = "https://bsky.social";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppPassword);
}

/// <summary>The C# Advent Facebook Page and a Page access token with <c>pages_manage_posts</c>.</summary>
public sealed class FacebookCredentials
{
    public string? PageId { get; set; }

    public string? PageToken { get; set; }

    /// <summary>The Graph API version requests use. Each version works for about two years.</summary>
    public string GraphVersion { get; set; } = "v24.0";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PageId) && !string.IsNullOrWhiteSpace(PageToken);
}
