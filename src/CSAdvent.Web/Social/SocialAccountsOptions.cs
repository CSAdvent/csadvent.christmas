using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Social;

/// <summary>
/// C# Advent's own social accounts: linked from the footer ("Follow C# Advent"), credited on shared links, and where
/// announcements go. Any of them can be left empty. Checked when the app starts (#188).
/// </summary>
public sealed class SocialAccountsOptions : IValidatableObject
{
    public const string SectionName = "Social";

    /// <summary>The Bluesky handle, such as <c>csadvent.christmas</c>.</summary>
    public string? Bluesky { get; set; }

    /// <summary>The Mastodon account, such as <c>@csadvent@mastodon.social</c>. Credited with <c>fediverse:creator</c>.</summary>
    public string? Mastodon { get; set; }

    public string? FacebookUrl { get; set; }

    public string? LinkedInUrl { get; set; }

    /// <summary>The Reddit account, without <c>u/</c>.</summary>
    public string? RedditUser { get; set; }

    /// <summary>The community announcements go to, without <c>r/</c>. Never r/csharp or r/dotnet (self-promotion rules).</summary>
    public string? Subreddit { get; set; }

    /// <summary>The X username, without <c>@</c>, such as <c>csadvent</c>. Credited with <c>twitter:site</c>.</summary>
    public string? X { get; set; }

    /// <summary>The server of the Mastodon account, such as <c>mastodon.social</c>.</summary>
    public string? MastodonServer => Mastodon?.Split('@', StringSplitOptions.RemoveEmptyEntries) is [_, var server] ? server : null;

    public string? MastodonUser => Mastodon?.Split('@', StringSplitOptions.RemoveEmptyEntries) is [var user, _] ? user : null;

    /// <summary>The accounts' public pages, for "Follow C# Advent" links, in the order they're shown.</summary>
    public IEnumerable<(string Name, string Url)> Profiles()
    {
        if (Bluesky is { Length: > 0 } bluesky)
        {
            yield return ("Bluesky", $"https://bsky.app/profile/{bluesky}");
        }

        if (MastodonServer is { } server && MastodonUser is { } user)
        {
            yield return ("Mastodon", $"https://{server}/@{user}");
        }

        if (FacebookUrl is { Length: > 0 } facebook)
        {
            yield return ("Facebook", facebook);
        }

        if (LinkedInUrl is { Length: > 0 } linkedIn)
        {
            yield return ("LinkedIn", linkedIn);
        }

        if (Subreddit is { Length: > 0 } subreddit)
        {
            yield return ("Reddit", $"https://www.reddit.com/r/{subreddit}");
        }

        if (X is { Length: > 0 } x)
        {
            yield return ("X", $"https://x.com/{x}");
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Without both parts, the footer's link, the share links and fediverse:creator would all be wrong.
        if (Mastodon is { Length: > 0 } && (MastodonUser is null || MastodonServer is null))
        {
            yield return new ValidationResult($"{SectionName}:Mastodon must be the whole account, such as @csadvent@mastodon.social.", [nameof(Mastodon)]);
        }

        foreach (var (name, url) in new[] { (nameof(FacebookUrl), FacebookUrl), (nameof(LinkedInUrl), LinkedInUrl) })
        {
            if (url is { Length: > 0 } && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            {
                yield return new ValidationResult($"{SectionName}:{name} must be the page's https:// address.", [name]);
            }
        }
    }
}
