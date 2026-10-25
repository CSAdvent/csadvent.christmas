using CSAdvent.Web.Claims;
using CSAdvent.Web.Social;

namespace CSAdvent.Web.Entries;

/// <summary>One of the places an author can be found, for their entry's page.</summary>
public sealed record AuthorLink(string Label, string Url);

/// <summary>Where an author's name links to, and the other places they can be found.</summary>
public static class AuthorLinks
{
    /// <summary>
    /// Their website, or their GitHub profile if they didn't give one. Archive posts keep the link they were published with,
    /// whatever it was.
    /// </summary>
    public static string? ForName(string? website, string? githubLogin) =>
        website ?? (string.IsNullOrEmpty(githubLogin) ? null : GitHub(githubLogin));

    public static string GitHub(string login) => $"https://github.com/{login}";

    /// <summary>Their website, GitHub profile and the social accounts they gave us, in that order.</summary>
    public static IReadOnlyList<AuthorLink> All(string? website, string? githubLogin, AuthorAccounts accounts)
    {
        var links = new List<AuthorLink>();
        if (website is not null && !SocialAccounts.IsSocialProfile(website))
        {
            links.Add(new("Website", website));
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
}
