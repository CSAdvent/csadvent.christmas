using System.Globalization;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Social;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Authors;

/// <summary>
/// Where an author's public page is (#203): an author linked to a GitHub account by their login, and an archive author who
/// isn't by their name, grouped the way the organizers' authors list groups them (<see cref="AuthorService.Key"/>).
/// </summary>
public static class AuthorPageLinks
{
    public const string Index = "authors";

    /// <summary>A linked author's page: <c>authors/CalvinAllen</c>.</summary>
    public static string ForLogin(string login) => $"authors/{Uri.EscapeDataString(login)}";

    /// <summary>An author who isn't linked to a GitHub account: <c>authors/name/ada%20lovelace</c>.</summary>
    public static string ForName(string name) => $"authors/name/{Uri.EscapeDataString(AuthorService.Key(name))}";

    /// <summary>The page of a post's author: the account's it's linked to, or else its author's name's.</summary>
    public static string For(string? githubLogin, string authorName) => githubLogin is null ? ForName(authorName) : ForLogin(githubLogin);

    /// <summary>The page a revealed post is on.</summary>
    public static string For(RevealedPost post) => For(post.GithubLogin, post.AuthorName);
}

/// <summary>An author in the public directory, with how many posts they've written and when.</summary>
/// <param name="Latest">Their most recent post, for the sitemap's date.</param>
public sealed record AuthorSummary(string Name, string Page, int Posts, int FirstYear, int LastYear, RevealedPost Latest)
{
    /// <summary>"2023", or "2018–2024".</summary>
    public string Years => FirstYear == LastYear
        ? FirstYear.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{FirstYear}–{LastYear}");
}

/// <summary>An author's public page: their links, and every revealed post of theirs, newest season first.</summary>
public sealed record AuthorPageView(string Name, string Page, IReadOnlyList<AuthorLink> Links, IReadOnlyList<RevealedPost> Posts)
{
    /// <summary>Their posts by season, newest first.</summary>
    public IEnumerable<IGrouping<int, RevealedPost>> Seasons => Posts.GroupBy(p => p.Year);

    /// <summary>"1 post, for C# Advent 2023.", "2 posts, for C# Advent 2023.", or "5 posts for C# Advent, from 2018 to 2024."</summary>
    public string Summary
    {
        get
        {
            var posts = Posts.Count == 1 ? "1 post" : string.Create(CultureInfo.InvariantCulture, $"{Posts.Count} posts");
            var first = Posts.Min(p => p.Year);
            var last = Posts.Max(p => p.Year);
            return first == last
                ? string.Create(CultureInfo.InvariantCulture, $"{posts}, for C# Advent {first}.")
                : string.Create(CultureInfo.InvariantCulture, $"{posts} for C# Advent, from {first} to {last}.");
        }
    }

    /// <summary>For the page's preview: "5 posts for C# Advent, from 2018 to 2024, by Ada Lovelace."</summary>
    public string Description => $"{Summary.TrimEnd('.')}, by {Name}.";
}

/// <summary>Groups revealed posts by author for the public pages, the same way everywhere.</summary>
public static class AuthorDirectory
{
    /// <summary>
    /// Everyone with a revealed post, by name: linked authors once each, whatever names they've written under, and archive
    /// authors who aren't linked grouped by name. A linked author's page is found by login whatever its case.
    /// </summary>
    public static IReadOnlyList<AuthorSummary> List(IEnumerable<RevealedPost> posts) =>
        [.. posts
            .GroupBy(AuthorPageLinks.For, StringComparer.OrdinalIgnoreCase)
            .Select(author =>
            {
                var latest = Latest(author);
                return new AuthorSummary(latest.AuthorName, AuthorPageLinks.For(latest), author.Count(), author.Min(p => p.Year), latest.Year, latest);
            })
            .OrderBy(a => a.Name, StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase))];

    /// <summary>Their most recent post, whose name and link are the ones they use now.</summary>
    public static RevealedPost Latest(IEnumerable<RevealedPost> posts) => posts.OrderByDescending(p => p.Year).ThenByDescending(p => p.Day).ThenByDescending(p => p.Part).First();
}

/// <summary>
/// The public author pages (#203): every author with a revealed post, and each one's posts across seasons with their links
/// (<see cref="AuthorLinks.All"/>). Built on <see cref="RevealedPosts"/>, so never the test season and nothing before a
/// post's reveal: an author whose only posts aren't revealed yet has no page.
/// </summary>
public sealed class AuthorPageService(RevealedPosts revealed, AdventDbContext db)
{
    public async Task<IReadOnlyList<AuthorSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        AuthorDirectory.List(await revealed.GetAsync(cancellationToken));

    /// <summary>A linked author's page, by GitHub login in any case, or null if they have no revealed posts.</summary>
    public async Task<AuthorPageView?> GetByLoginAsync(string login, CancellationToken cancellationToken = default)
    {
        var posts = (await revealed.GetAsync(cancellationToken)).Where(p => string.Equals(p.GithubLogin, login, StringComparison.OrdinalIgnoreCase)).ToList();
        if (posts.Count == 0)
        {
            return null;
        }

        // A login is one account at a time, but an account renamed since its last sign-in keeps its old login here: the
        // account with the latest post has it now.
        var contributorId = AuthorDirectory.Latest(posts).ContributorId;
        posts = [.. posts.Where(p => p.ContributorId == contributorId)];
        var latest = AuthorDirectory.Latest(posts);
        var accounts = await db.Contributors
            .AsNoTracking()
            .Where(c => c.Id == contributorId)
            .Select(c => new AuthorAccounts(c.BlueskyHandle, c.MastodonHandle, c.LinkedInUrl, c.FacebookUrl, c.RedditUsername, c.XUsername))
            .SingleAsync(cancellationToken);

        return new AuthorPageView(latest.AuthorName, AuthorPageLinks.ForLogin(latest.GithubLogin!), AuthorLinks.All(Website(posts), latest.GithubLogin, accounts), posts);
    }

    /// <summary>An archive author who isn't linked, by their name's key, or null if they have no revealed posts.</summary>
    public async Task<AuthorPageView?> GetByNameAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = AuthorService.Key(key);
        var posts = (await revealed.GetAsync(cancellationToken)).Where(p => p.GithubLogin is null && AuthorService.Key(p.AuthorName) == normalized).ToList();
        if (posts.Count == 0)
        {
            return null;
        }

        var latest = AuthorDirectory.Latest(posts);
        return new AuthorPageView(latest.AuthorName, AuthorPageLinks.ForName(latest.AuthorName), AuthorLinks.All(Website(posts), null, AuthorAccounts.None), posts);
    }

    /// <summary>
    /// Where an archive author's page went once an organizer linked them to a GitHub account: the account's page, if every
    /// revealed post under that name is now on it. Search engines and old links then still find them.
    /// </summary>
    public async Task<string?> FindLinkedPageAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = AuthorService.Key(key);
        var logins = (await revealed.GetAsync(cancellationToken))
            .Where(p => AuthorService.Key(p.AuthorName) == normalized)
            .Select(p => p.GithubLogin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return logins is [{ } login] ? AuthorPageLinks.ForLogin(login) : null;
    }

    /// <summary>The website they gave with their latest post that has one.</summary>
    private static string? Website(IEnumerable<RevealedPost> posts) =>
        posts.OrderByDescending(p => p.Year).ThenByDescending(p => p.Day).Select(p => p.AuthorUrl).FirstOrDefault(url => url is not null);
}
