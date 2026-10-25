using CSAdvent.Web.Components.Pages.Entries;
using CSAdvent.Web.Entries;

namespace CSAdvent.UnitTests.Entries;

public sealed class PostSummariesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Spans,\n  everywhere ", "Spans, everywhere")]
    public void ASummaryIsKeptOnOneLine(string? summary, string? cleaned) =>
        Assert.Equal(cleaned, PostSummaries.Clean(summary));

    [Theory]
    [InlineData("Spans everywhere", "Spans everywhere.")]
    [InlineData("Spans everywhere.", "Spans everywhere.")]
    [InlineData("Why spans?", "Why spans?")]
    [InlineData("Spans!", "Spans!")]
    public void ASummaryEndsLikeASentenceBeforeTheByline(string summary, string sentence) =>
        Assert.Equal(sentence, PostSummaries.AsSentence(summary));

    [Fact]
    public void TheEntryPagesDescriptionStartsWithItsSummary()
    {
        var entry = new EntryView(1, 2026, 6, 2, 2, true, DateTimeOffset.UnixEpoch, "Patterns", "https://example.com/p", "Ada Lovelace", null, [], [], false);

        Assert.Equal("By Ada Lovelace, for C# Advent 2026: December 6, 2026, Post 2.", EntryPage.Description(entry));
        Assert.Equal(
            "Switch expressions and more. By Ada Lovelace, for C# Advent 2026: December 6, 2026, Post 2.",
            EntryPage.Description(entry with { PostSummary = "Switch expressions and more" }));
    }
}
