using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Social;

namespace CSAdvent.Web.Entries;

/// <summary>One of the places an author can be found, for their entry's page.</summary>
public sealed record AuthorLink(string Label, string Url);

/// <summary>Where an author's name links to, and the other places they can be found.</summary>
public static partial class AuthorLinks
{
    /// <summary>
    /// Their website, or their GitHub profile if they didn't give one. Archive posts keep the link they were published with,
    /// whatever it was, unless it was where they signed up (<see cref="Website"/>).
    /// </summary>
    public static string? ForName(string? website, string? githubLogin) =>
        Website(website) ?? (string.IsNullOrEmpty(githubLogin) ? null : GitHub(githubLogin));

    public static string GitHub(string login) => $"https://github.com/{login}";

    /// <summary>
    /// The author's link, unless it's where they signed up rather than a site of theirs: in the early seasons that was often
    /// their sign-up tweet (<c>/status/</c>) or their comment on the sign-up post (<c>#comment-</c>), which says nothing
    /// about them, and tweets now need an X login to read (#166). Those count as no website.
    /// </summary>
    public static string? Website(string? url) => url is null || IsSignUpLink(url) ? null : url;

    /// <summary>A link to a single post or comment (a tweet, or a comment on a blog post), not to a person or their site.</summary>
    public static bool IsSignUpLink(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Fragment.StartsWith("#comment-", StringComparison.OrdinalIgnoreCase) || StatusPath().IsMatch(uri.AbsolutePath));

    /// <summary>Their website, GitHub profile and the social accounts they gave us, in that order.</summary>
    public static IReadOnlyList<AuthorLink> All(string? website, string? githubLogin, AuthorAccounts accounts)
    {
        var links = new List<AuthorLink>();
        if (Website(website) is { } site && !SocialAccounts.IsSocialProfile(site))
        {
            links.Add(new("Website", site));
        }

        if (!string.IsNullOrEmpty(githubLogin))
        {
            links.Add(new("GitHub", GitHub(githubLogin)));
        }

        foreach (var (platform, account) in new (SocialPlatform, string?)[]
        {
            (SocialPlatform.Bluesky, accounts.Bluesky),
            (SocialPlatform.Mastodon, accounts.Mastodon),
            (SocialPlatform.LinkedIn, accounts.LinkedIn),
            (SocialPlatform.Facebook, accounts.Facebook),
            (SocialPlatform.Reddit, accounts.Reddit),
            (SocialPlatform.X, accounts.X),
        })
        {
            if (account is not null)
            {
                links.Add(new(platform.ToString(), SocialAccounts.ProfileUrl(platform, account)));
            }
        }

        return links;
    }

    // "/madSimonJ/status/1181174805703385088" (twitter.com and x.com; "statuses" on older links)
    [GeneratedRegex(@"^/[^/]+/status(?:es)?/\d+", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPath();
}
