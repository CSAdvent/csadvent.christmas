namespace CSAdvent.Web.Entries;

/// <summary>
/// A post's optional one-line summary (#208), from My spots or the entry editor. It's public once the post is revealed, and
/// never before (#5), like the title.
/// </summary>
public static class PostSummaries
{
    /// <summary>The summary on one line, without extra spaces, or nothing if it's blank.</summary>
    public static string? Clean(string? summary) =>
        string.IsNullOrWhiteSpace(summary) ? null : string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The summary ending like a sentence, so more can follow it, as in a feed item's description.</summary>
    public static string AsSentence(string summary) =>
        summary.EndsWith('.') || summary.EndsWith('!') || summary.EndsWith('?') || summary.EndsWith('…') ? summary : $"{summary}.";
}
