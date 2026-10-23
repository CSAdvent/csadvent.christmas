using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

/// <summary>
/// A row in the authors directory: a person linked to a GitHub account (every spot of theirs, under whatever names), or an
/// archive author not linked yet, grouped by name.
/// </summary>
public sealed record AuthorRow(int? ContributorId, string? Key, string Name, string? GithubLogin, int Posts, IReadOnlyList<AuthorSeason> Seasons, string? SuggestedLogin)
{
    public string Profile => ContributorId is { } id ? $"manage/authors/{id}" : $"manage/authors/unlinked?key={Uri.EscapeDataString(Key!)}";
}

/// <summary>A season an author took part in, and the post they published in it, if they did. Two posts in a season are two of these.</summary>
public sealed record AuthorSeason(int Year, string? PostUrl, string? PostTitle);

/// <summary>One of an author's spots, published or not, with what the calendar shows for it.</summary>
public sealed record AuthorPost(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

/// <summary>Everything the organizers know about an author, for their profile page.</summary>
public sealed record AuthorProfile(
    int? ContributorId,
    string? Key,
    string Name,
    string? GithubLogin,
    string? Email,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Links,
    IReadOnlyList<AuthorPost> Posts,
    string? SuggestedLogin,
    IReadOnlyList<AuthorRow> OtherAuthors)
{
    public int Published => Posts.Count(p => p.PostUrl is not null);
}

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
/// Everyone who's written for C# Advent, for the organizers: the directory, each author's profile, and linking the archive's
/// authors to GitHub accounts so returning authors find their history on My spots. Posts from 2026 on are linked when their
/// claim is approved; earlier ones are linked here by an organizer, ahead of time. Someone who hasn't signed in yet gets a
/// contributor record with only their public GitHub user ID and login, which their first sign-in picks up.
/// </summary>
public sealed class AuthorService(AdventDbContext db, AdventSchedule schedule, GitHubUsers github, TimeProvider timeProvider)
{
    /// <summary>Every author: linked people, then archive authors not linked yet, each by name.</summary>
    public async Task<IReadOnlyList<AuthorRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var spots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear != Season.TestYear && s.AuthorName != null)
            .Select(s => new { s.Id, s.SeasonYear, s.Day, s.Part, AuthorName = s.AuthorName!, s.AuthorUrl, s.PostTitle, s.PostUrl, s.ContributorId, Login = s.Contributor != null ? s.Contributor.GithubLogin : null })
            .ToListAsync(cancellationToken);

        var linked = spots
            .Where(s => s.ContributorId is not null)
            .GroupBy(s => (Id: s.ContributorId!.Value, s.Login))
            .Select(person => new AuthorRow(
                person.Key.Id,
                null,
                person.OrderByDescending(s => s.SeasonYear).First().AuthorName,
                person.Key.Login,
                person.Count(s => s.PostUrl is not null),
                [.. person.OrderBy(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part).Select(s => new AuthorSeason(s.SeasonYear, s.PostUrl, s.PostTitle))],
                null));

        // Unlinked authors are the archive's: a season still running links its authors when their claims are approved.
        var unlinked = spots
            .Where(s => s.ContributorId is null && s.PostUrl is not null && schedule.IsSeasonOver(s.SeasonYear))
            .GroupBy(s => Key(s.AuthorName))
            .Select(group => new AuthorRow(
                null,
                group.Key,
                MostUsed(group.Select(s => s.AuthorName)),
                null,
                group.Count(),
                [.. group.OrderBy(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part).Select(s => new AuthorSeason(s.SeasonYear, s.PostUrl, s.PostTitle))],
                group.Select(s => SuggestLogin(s.AuthorUrl)).OfType<string>().FirstOrDefault()));

        return [.. linked.Concat(unlinked).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A linked author's profile: every spot of theirs in any season, and their account.</summary>
    public async Task<AuthorProfile?> GetAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        if (await db.Contributors.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken) is not { } contributor)
        {
            return null;
        }

        var posts = await PostsAsync(db.Slots.Where(s => s.ContributorId == contributorId), cancellationToken);
        var names = posts.Select(p => p.AuthorName).OfType<string>().Distinct().ToList();
        return new AuthorProfile(
            contributor.Id,
            null,
            contributor.DisplayName ?? posts.FirstOrDefault()?.AuthorName ?? contributor.GithubLogin,
            contributor.GithubLogin,
            contributor.EmailVerifiedAt is not null ? contributor.Email : null,
            names,
            [.. posts.Select(p => p.AuthorUrl).OfType<string>().Distinct()],
            posts,
            null,
            await CandidatesAsync(names, cancellationToken));
    }

    /// <summary>An archive author who isn't linked yet: their published posts in seasons that are over.</summary>
    public async Task<AuthorProfile?> GetUnlinkedAsync(string key, CancellationToken cancellationToken = default)
    {
        var posts = (await UnlinkedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName!) == key).ToList();
        if (posts.Count == 0)
        {
            return null;
        }

        return new AuthorProfile(
            null,
            key,
            MostUsed(posts.Select(p => p.AuthorName!)),
            null,
            null,
            [.. posts.Select(p => p.AuthorName).OfType<string>().Distinct()],
            [.. posts.Select(p => p.AuthorUrl).OfType<string>().Distinct()],
            posts,
            posts.Select(p => SuggestLogin(p.AuthorUrl)).OfType<string>().FirstOrDefault(),
            []);
    }

    /// <summary>
    /// Adds an unlinked archive author's posts to a linked author: the same person under another name, such as
    /// "Calvin A. Allen" as well as "Calvin Allen". Returns false if there's no such unlinked author.
    /// </summary>
    public async Task<bool> CombineAsync(int contributorId, string key, CancellationToken cancellationToken = default)
    {
        var slotIds = (await UnlinkedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName!) == key).Select(p => p.SlotId).ToList();
        if (slotIds.Count == 0 || !await db.Contributors.AnyAsync(c => c.Id == contributorId, cancellationToken))
        {
            return false;
        }

        await db.Slots.Where(s => slotIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, contributorId), cancellationToken);
        return true;
    }

    /// <summary>Links every finished-season post by the author to the GitHub account, creating its contributor record if needed.</summary>
    public async Task<(AuthorLinkOutcome Outcome, int? ContributorId)> LinkAsync(string key, string login, CancellationToken cancellationToken = default)
    {
        var slotIds = (await UnlinkedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName!) == key).Select(p => p.SlotId).ToList();
        if (slotIds.Count == 0)
        {
            return (AuthorLinkOutcome.NoSuchAuthor, null);
        }

        if (GitHubLogins.Normalize(login) is not { } normalized)
        {
            return (AuthorLinkOutcome.NoSuchAccount, null);
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
                return (AuthorLinkOutcome.GitHubUnavailable, null);
            }

            if (account is null)
            {
                return (AuthorLinkOutcome.NoSuchAccount, null);
            }

            contributorId = await FindOrAddAsync(account, cancellationToken);
        }

        await db.Slots.Where(s => slotIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, contributorId), cancellationToken);
        return (AuthorLinkOutcome.Linked, contributorId);
    }

    /// <summary>
    /// Unlinks the author's posts in seasons that are over, so they're an archive author again. Spots in a season still
    /// running stay theirs, because that's how they manage them on My spots.
    /// </summary>
    public async Task UnlinkAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        var finished = (await db.Slots.Where(s => s.ContributorId == contributorId).Select(s => new { s.Id, s.SeasonYear }).ToListAsync(cancellationToken))
            .Where(s => schedule.IsSeasonOver(s.SeasonYear))
            .Select(s => s.Id)
            .ToList();
        await db.Slots.Where(s => finished.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, (int?)null), cancellationToken);
    }

    /// <summary>
    /// Changes the name and link on an unlinked archive author's posts. Returns their key under the new name, or null if
    /// there's no such author. A linked author's name and link are on their profile (<see cref="Contributors.ProfileService"/>).
    /// </summary>
    public async Task<string?> RenameAsync(string key, CalendarNameForm form, CancellationToken cancellationToken = default)
    {
        var slotIds = (await UnlinkedPostsAsync(cancellationToken)).Where(p => Key(p.AuthorName!) == key).Select(p => p.SlotId).ToList();
        if (slotIds.Count == 0)
        {
            return null;
        }

        var name = form.Name.Trim();
        var link = string.IsNullOrWhiteSpace(form.Link) ? null : form.Link.Trim();
        await db.Slots.Where(s => slotIds.Contains(s.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorName, name).SetProperty(x => x.AuthorUrl, link), cancellationToken);
        return Key(name);
    }

    /// <summary>
    /// The unlinked archive authors, for "also wrote as": those sharing a word of the author's names (a surname, usually)
    /// first, then everyone else.
    /// </summary>
    private async Task<IReadOnlyList<AuthorRow>> CandidatesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var words = names.SelectMany(n => Key(n).Split(' ')).Where(w => w.Length > 2).ToHashSet();
        return [.. (await UnlinkedPostsAsync(cancellationToken))
            .GroupBy(p => Key(p.AuthorName!))
            .Select(group => new AuthorRow(
                null,
                group.Key,
                MostUsed(group.Select(p => p.AuthorName!)),
                null,
                group.Count(),
                [.. group.OrderBy(p => p.Year).ThenBy(p => p.Day).Select(p => new AuthorSeason(p.Year, p.PostUrl, p.PostTitle))],
                null))
            .OrderByDescending(a => a.Key!.Split(' ').Any(words.Contains))
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether an unlinked author looks like the same person, for sorting "also wrote as" suggestions first.</summary>
    public static bool SharesAName(AuthorRow candidate, IEnumerable<string> names)
    {
        var words = names.SelectMany(n => Key(n).Split(' ')).Where(w => w.Length > 2).ToHashSet();
        return candidate.Key is { } key && key.Split(' ').Any(words.Contains);
    }

    /// <summary>How an author is grouped: their name without case or extra spaces, so "Ada  Lovelace" and "ada lovelace" are one author.</summary>
    public static string Key(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();

    /// <summary>The login in a GitHub profile link (github.com/login), if the author's link is one.</summary>
    public static string? SuggestLogin(string? authorUrl) =>
        Uri.TryCreate(authorUrl, UriKind.Absolute, out var uri) && uri.Host is "github.com" or "www.github.com"
            && uri.AbsolutePath.Trim('/') is { Length: > 0 } path && !path.Contains('/')
            ? GitHubLogins.Normalize(path)
            : null;

    private static string MostUsed(IEnumerable<string> names) => names.GroupBy(n => n).OrderByDescending(n => n.Count()).First().Key;

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

    private async Task<List<AuthorPost>> UnlinkedPostsAsync(CancellationToken cancellationToken) =>
        [.. (await PostsAsync(db.Slots.Where(s => s.ContributorId == null && s.PostUrl != null), cancellationToken)).Where(p => schedule.IsSeasonOver(p.Year))];

    private static async Task<List<AuthorPost>> PostsAsync(IQueryable<Slot> slots, CancellationToken cancellationToken) =>
        await slots
            .AsNoTracking()
            .Where(s => s.SeasonYear != Season.TestYear && s.AuthorName != null)
            .OrderByDescending(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new AuthorPost(s.Id, s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.AuthorName, s.AuthorUrl, s.PostTitle, s.PostUrl))
            .ToListAsync(cancellationToken);
}

/// <summary>The name and link the calendar shows for an unlinked archive author, on every one of their posts.</summary>
public sealed class CalendarNameForm
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(100, ErrorMessage = "Keep the name to 100 characters or fewer.")]
    public string Name { get; set; } = "";

    [StringLength(500, ErrorMessage = "Keep the link to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Enter a full web address that starts with https:// or http://.")]
    public string? Link { get; set; }
}

/// <summary>The GitHub login an organizer links an unlinked author to, on the author's page.</summary>
public sealed class GitHubLoginForm
{
    [Required(ErrorMessage = "Enter their GitHub login.")]
    public string Login { get; set; } = "";
}

/// <summary>An unlinked archive author to add to a linked author ("also wrote as").</summary>
public sealed class CombineForm
{
    [Required(ErrorMessage = "Pick the name they also wrote as.")]
    public string Key { get; set; } = "";
}

public sealed class UnlinkForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm you want to unlink them.")]
    public bool Confirm { get; set; }
}
