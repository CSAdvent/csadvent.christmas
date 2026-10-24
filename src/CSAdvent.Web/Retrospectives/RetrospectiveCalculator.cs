using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;

namespace CSAdvent.Web.Retrospectives;

/// <summary>A published post from a finished season, for counting posts and authors.</summary>
public sealed record PublishedPost(int SlotId, int Year, int Day, int Part, string Title, string Author, int? ContributorId);

/// <summary>A counted visit or click on one of the season's entries.</summary>
public sealed record RecordedHit(int SlotId, EntryHitKind Kind, string? Via, DateTimeOffset At);

/// <summary>What <see cref="RetrospectiveCalculator"/> works from, read by <see cref="RetrospectiveService"/>.</summary>
/// <param name="Posts">Every finished season's posts up to this one, never the test season's.</param>
/// <param name="Hits">This season's visits and clicks from the first reveal to the end of December.</param>
/// <param name="December1">The season's December 1, for the clicks per day.</param>
/// <param name="LocalDate">The date in the reveal time zone.</param>
public sealed record RetrospectiveInput(
    int Year,
    int SlotsPerDay,
    int Spots,
    IReadOnlyList<PublishedPost> Posts,
    IReadOnlyList<RecordedHit> Hits,
    DateOnly December1,
    Func<DateTimeOffset, DateOnly> LocalDate,
    int Requests,
    int RequestsFirstDay,
    int BackupsSteppedIn,
    IReadOnlyList<PlatformPosts> SocialPosts);

/// <summary>
/// Turns a season's posts and traffic into its <see cref="RetrospectiveStats"/>.
/// </summary>
/// <remarks>
/// Authors are told apart by their GitHub account where a post is linked to one, and otherwise by their name, whatever its
/// case or spacing (<see cref="AuthorService.Key"/>), so archive posts that were never linked still count. Someone is a
/// returning author if either matches a post from an earlier season.
/// </remarks>
public static class RetrospectiveCalculator
{
    /// <summary>The source of clicks without a tag, or from an entry page visited without one (<c>site</c>).</summary>
    public const string Direct = "direct";

    public const int MostReadCount = 10;

    public static RetrospectiveStats Calculate(RetrospectiveInput input)
    {
        var seasons = input.Posts
            .Where(p => p.Year <= input.Year)
            .GroupBy(p => p.Year)
            .OrderBy(g => g.Key)
            .Select(g => (Year: g.Key, Posts: g.Count(), Authors: AuthorsOf(g)))
            .ToList();

        // Everyone who'd written before each season, to tell first-time authors from returning ones.
        var history = new List<SeasonHistory>();
        var seenIds = new HashSet<int>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var season in seasons)
        {
            var firstTime = season.Authors.Count(a => !a.Ids.Overlaps(seenIds) && !a.Keys.Overlaps(seenKeys));
            history.Add(new SeasonHistory(season.Year, season.Posts, season.Authors.Count, firstTime));
            foreach (var author in season.Authors)
            {
                seenIds.UnionWith(author.Ids);
                seenKeys.UnionWith(author.Keys);
            }
        }

        var thisSeason = seasons.SingleOrDefault(s => s.Year == input.Year);
        var authors = thisSeason.Authors ?? [];
        var bySeasonsWritten = authors
            .Select(author => seasons.Count(season => season.Authors.Any(other => other.Overlaps(author))))
            .ToList();

        var clicks = input.Hits.Where(h => h.Kind is EntryHitKind.Click).ToList();
        var posts = input.Posts.Where(p => p.Year == input.Year).ToDictionary(p => p.SlotId);

        return new RetrospectiveStats(
            input.Year,
            input.SlotsPerDay,
            input.Spots,
            thisSeason.Posts,
            authors.Count,
            history.SingleOrDefault(h => h.Year == input.Year)?.FirstTimeAuthors ?? 0,
            bySeasonsWritten.Count == 0
                ? []
                : [.. Enumerable.Range(1, bySeasonsWritten.Max()).Select(count => new SeasonsWritten(count, bySeasonsWritten.Count(c => c == count)))],
            input.BackupsSteppedIn,
            input.Requests,
            input.RequestsFirstDay,
            input.Hits.Count(h => h.Kind is EntryHitKind.View),
            clicks.Count,
            input.Hits.Count == 0 ? [] : ClicksPerDay(clicks, input),
            [.. clicks
                .GroupBy(h => h.Via is null or "site" ? Direct : h.Via)
                .Select(g => new SourceClicks(g.Key, g.Count()))
                .OrderByDescending(s => s.Clicks).ThenBy(s => s.Source, StringComparer.Ordinal)],
            [.. clicks
                .Where(h => posts.ContainsKey(h.SlotId))
                .GroupBy(h => h.SlotId)
                .Select(g => (Post: posts[g.Key], Clicks: g.Count()))
                .OrderByDescending(e => e.Clicks).ThenBy(e => e.Post.Day).ThenBy(e => e.Post.Part)
                .Take(MostReadCount)
                .Select(e => new MostReadEntry(e.Post.Day, e.Post.Part, e.Post.Title, e.Post.Author, e.Clicks))],
            [.. input.SocialPosts.Where(p => p.Posts > 0)],
            history);
    }

    private static int[] ClicksPerDay(List<RecordedHit> clicks, RetrospectiveInput input)
    {
        var days = new int[31];
        foreach (var click in clicks)
        {
            var day = input.LocalDate(click.At).DayNumber - input.December1.DayNumber;
            if (day is >= 0 and < 31)
            {
                days[day]++;
            }
        }

        return days;
    }

    /// <summary>A season's different authors. Posts with the same GitHub account or the same name are the same author.</summary>
    private static List<Author> AuthorsOf(IEnumerable<PublishedPost> posts)
    {
        var authors = new List<Author>();
        foreach (var post in posts)
        {
            var key = AuthorService.Key(post.Author);
            var author = authors.FirstOrDefault(a => (post.ContributorId is { } id && a.Ids.Contains(id)) || a.Keys.Contains(key));
            if (author is null)
            {
                author = new Author();
                authors.Add(author);
            }

            author.Keys.Add(key);
            if (post.ContributorId is { } contributorId)
            {
                author.Ids.Add(contributorId);
            }
        }

        return authors;
    }

    private sealed class Author
    {
        public HashSet<int> Ids { get; } = [];

        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

        public bool Overlaps(Author other) => Ids.Overlaps(other.Ids) || Keys.Overlaps(other.Keys);
    }
}
