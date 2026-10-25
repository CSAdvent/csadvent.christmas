using System.Globalization;
using System.Text;

namespace CSAdvent.Web.Entries;

/// <summary>
/// Searching every post (#204) by title, author and summary, in one season or all of them. Only revealed posts are ever
/// candidates (<see cref="RevealedPosts"/>), and they're filtered before any matching, so a search can't tell anything about a
/// post before its reveal: not in its results, its count, or anything else about the page.
/// </summary>
public static class ArchiveSearch
{
    /// <summary>The longest query that's read; anything after it is ignored.</summary>
    public const int MaxQueryLength = 100;

    /// <summary>
    /// The posts with every word of the query in their title, author's name or summary, ignoring case and accents, newest
    /// season first. With no words, every post of the season; with no words and no season, nothing.
    /// </summary>
    public static IReadOnlyList<RevealedPost> Find(IEnumerable<RevealedPost> revealed, string? query, int? year)
    {
        var words = Words(query);
        if (words.Count == 0 && year is null)
        {
            return [];
        }

        return [.. revealed
            .Where(post => year is null || post.Year == year)
            .Where(post => Matches(post, words))
            .OrderByDescending(post => post.Year).ThenBy(post => post.Day).ThenBy(post => post.Part)];
    }

    /// <summary>The query's words, folded like the posts' text.</summary>
    public static IReadOnlyList<string> Words(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : [.. Fold(query.Length > MaxQueryLength ? query[..MaxQueryLength] : query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];

    /// <summary>Lowercase, without accents, and with every kind of space a plain one: "Gérald Barré" is "gerald barre".</summary>
    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) is not UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character));
            }
        }

        return folded.ToString();
    }

    private static bool Matches(RevealedPost post, IReadOnlyList<string> words)
    {
        var text = Fold($"{post.Title} {post.AuthorName} {post.Summary}");
        return words.All(word => text.Contains(word, StringComparison.Ordinal));
    }
}
