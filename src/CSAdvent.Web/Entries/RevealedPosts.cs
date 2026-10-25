using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Entries;

/// <summary>A post readers can see: revealed, with a title, link and author, in a real season.</summary>
/// <param name="GithubLogin">The GitHub login of the contributor the post is linked to, if it's linked.</param>
public sealed record RevealedPost(
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string Title,
    string? Summary,
    string AuthorName,
    string? AuthorUrl,
    int? ContributorId,
    string? GithubLogin)
{
    /// <summary>For example "December 6 · Post 2", within its season.</summary>
    public string Spot => SlotsPerDay > 1 ? $"December {Day} · Post {Part}" : $"December {Day}";

    /// <summary>Its entry page, relative to the base URL.</summary>
    public string Page => EntryLinks.Page(Year, Day, Part);
}

/// <summary>
/// Every post readers can see, from every season but the test season: what author pages, the archive search and "a random
/// post" read. The reveal is checked here, in C# rather than SQL (#5), before anything else looks at a post, so nothing built
/// on this can say anything about a post before its reveal, not even that it exists.
/// </summary>
public sealed class RevealedPosts(AdventDbContext db, AdventSchedule schedule)
{
    /// <summary>Newest season first, then in calendar order.</summary>
    public async Task<IReadOnlyList<RevealedPost>> GetAsync(CancellationToken cancellationToken = default)
    {
        var posts = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear != Season.TestYear && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .Select(s => new RevealedPost(
                s.Id,
                s.SeasonYear,
                s.Day,
                s.Part,
                s.Season.SlotsPerDay,
                s.PostTitle!,
                s.PostSummary,
                s.AuthorName!,
                s.AuthorUrl,
                s.ContributorId,
                s.Contributor != null ? s.Contributor.GithubLogin : null))
            .ToListAsync(cancellationToken);

        return [.. posts.Where(p => IsShown(p.Year, p.Day, schedule)).OrderByDescending(p => p.Year).ThenBy(p => p.Day).ThenBy(p => p.Part)];
    }

    /// <summary>
    /// Whether readers can see a day's posts here: once they're revealed, and never the test season's, even for its testers,
    /// because these pages are C# Advent's history (and search engines and feed readers remember them).
    /// </summary>
    public static bool IsShown(int year, int day, AdventSchedule schedule) => year != Season.TestYear && schedule.IsRevealed(year, day);
}
