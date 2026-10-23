using CSAdvent.Web.Auth;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

/// <summary>
/// An author named on the archive's published posts, by name (whatever its case or spacing), with the GitHub account their
/// posts are linked to, if any, and a login guessed from a GitHub profile link.
/// </summary>
public sealed record ArchiveAuthor(string Key, string Name, int Posts, IReadOnlyList<int> Years, IReadOnlyList<string> Links, string? GithubLogin, string? SuggestedLogin);

public enum AuthorLinkOutcome
{
    Linked,
    NoSuchAuthor,

    /// <summary>It isn't a GitHub login, or GitHub has no such account.</summary>
    NoSuchAccount,

    /// <summary>GitHub didn't answer, or is limiting how often we ask.</summary>
    GitHubUnavailable,
}

/// <summary>
/// Links the archive's authors to GitHub accounts, so returning authors find their C# Advent history on My spots, and
/// organizers can see who's written before. Posts from 2026 on are linked when their claim is approved; this is for the
/// seasons before, done by an organizer ahead of time. Someone who hasn't signed in yet gets a contributor record with only
/// their public GitHub user ID and login, which their first sign-in picks up.
/// </summary>
public sealed class AuthorLinkService(AdventDbContext db, AdventSchedule schedule, GitHubUsers github, TimeProvider timeProvider)
{
    /// <summary>Every author of a published post in a finished season, unlinked first, then by name.</summary>
    public async Task<IReadOnlyList<ArchiveAuthor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var posts = await PublishedPostsAsync(cancellationToken);
        return [.. posts
            .GroupBy(p => Key(p.AuthorName))
            .Select(group => new ArchiveAuthor(
                group.Key,
                group.GroupBy(p => p.AuthorName).OrderByDescending(names => names.Count()).First().Key,
                group.Count(),
                [.. group.Select(p => p.Year).Distinct().Order()],
                [.. group.Select(p => p.AuthorUrl).OfType<string>().Distinct()],
                string.Join(", ", group.Select(p => p.GithubLogin).OfType<string>().Distinct()) is { Length: > 0 } logins ? logins : null,
                group.Select(p => SuggestLogin(p.AuthorUrl)).OfType<string>().FirstOrDefault()))
            .OrderBy(a => a.GithubLogin is not null)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Links every finished-season post by the author to the GitHub account, creating its contributor record if needed.</summary>
    public async Task<AuthorLinkOutcome> LinkAsync(string key, string login, CancellationToken cancellationToken = default)
    {
        var slotIds = (await PublishedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName) == key).Select(p => p.SlotId).ToList();
        if (slotIds.Count == 0)
        {
            return AuthorLinkOutcome.NoSuchAuthor;
        }

        if (GitHubLogins.Normalize(login) is not { } normalized)
        {
            return AuthorLinkOutcome.NoSuchAccount;
        }

        int contributorId;
        if (await db.Contributors.Where(c => c.GithubLogin.ToLower() == normalized).Select(c => (int?)c.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
        {
            contributorId = existing;
        }
        else
        {
            GitHubAccount? account;
            try
            {
                account = await github.FindAsync(normalized, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return AuthorLinkOutcome.GitHubUnavailable;
            }

            if (account is null)
            {
                return AuthorLinkOutcome.NoSuchAccount;
            }

            contributorId = await FindOrAddAsync(account, cancellationToken);
        }

        await db.Slots.Where(s => slotIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, contributorId), cancellationToken);
        return AuthorLinkOutcome.Linked;
    }

    public async Task UnlinkAsync(string key, CancellationToken cancellationToken = default)
    {
        var slotIds = (await PublishedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName) == key).Select(p => p.SlotId).ToList();
        await db.Slots.Where(s => slotIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, (int?)null), cancellationToken);
    }

    /// <summary>How an author is grouped: their name without case or extra spaces, so "Ada  Lovelace" and "ada lovelace" are one author.</summary>
    public static string Key(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();

    /// <summary>The login in a GitHub profile link (github.com/login), if the author's link is one.</summary>
    public static string? SuggestLogin(string? authorUrl) =>
        Uri.TryCreate(authorUrl, UriKind.Absolute, out var uri) && uri.Host is "github.com" or "www.github.com"
            && uri.AbsolutePath.Trim('/') is { Length: > 0 } path && !path.Contains('/')
            ? GitHubLogins.Normalize(path)
            : null;

    private async Task<int> FindOrAddAsync(GitHubAccount account, CancellationToken cancellationToken)
    {
        // Someone who signed in once under an older login is still the same person.
        if (await db.Contributors.SingleOrDefaultAsync(c => c.GithubUserId == account.Id, cancellationToken) is { } contributor)
        {
            return contributor.Id;
        }

        contributor = new Contributor { GithubUserId = account.Id, GithubLogin = account.Login, CreatedAt = timeProvider.GetUtcNow() };
        db.Contributors.Add(contributor);
        await db.SaveChangesAsync(cancellationToken);
        return contributor.Id;
    }

    private async Task<List<PublishedPost>> PublishedPostsAsync(CancellationToken cancellationToken)
    {
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear != Season.TestYear && s.AuthorName != null && s.PostUrl != null)
            .Select(s => new PublishedPost(s.Id, s.SeasonYear, s.AuthorName!, s.AuthorUrl, s.Contributor != null ? s.Contributor.GithubLogin : null))
            .ToListAsync(cancellationToken);

        // Only seasons that are over: a season still running links its authors when their claims are approved.
        return [.. posts.Where(p => schedule.IsSeasonOver(p.Year))];
    }

    private sealed record PublishedPost(int SlotId, int Year, string AuthorName, string? AuthorUrl, string? GithubLogin);
}
