using CSAdvent.Web.Entries;

namespace CSAdvent.UnitTests.Entries;

public sealed class ArchiveSearchTests
{
    private static readonly RevealedPost[] Posts =
    [
        Post(2025, 3, "Blazor and .NET Aspire, together", "Ada Lovelace"),
        Post(2024, 6, "Source generators in practice", "Gérald Barré", summary: "Interceptors and incremental generators."),
        Post(2024, 1, "What's new in Blazor", "Bob Builder"),
        Post(2019, 9, "Span<T> all the things", "Ada Lovelace"),
    ];

    private static RevealedPost Post(int year, int day, string title, string author, string? summary = null) =>
        new(year * 100 + day, year, day, 1, 2, title, summary, author, null, null, null);

    [Fact]
    public void EveryWordHasToMatchTheTitleAuthorOrSummary()
    {
        Assert.Equal(["Blazor and .NET Aspire, together", "What's new in Blazor"], Titles("blazor", null));
        Assert.Equal(["Blazor and .NET Aspire, together"], Titles("BLAZOR lovelace", null));
        Assert.Equal(["Source generators in practice"], Titles("incremental", null));
        Assert.Empty(Titles("blazor generators", null));
    }

    [Fact]
    public void AccentsAndSpacesDoNotMatter()
    {
        Assert.Equal(["Source generators in practice"], Titles("  gerald   barre ", null));
        Assert.Equal(["Source generators in practice"], Titles("Gérald", null));
    }

    [Fact]
    public void ASeasonNarrowsItDownOrListsItsPosts()
    {
        Assert.Equal(["Span<T> all the things"], Titles("ada", 2019));
        Assert.Equal(["What's new in Blazor", "Source generators in practice"], Titles(null, 2024));
        Assert.Empty(Titles(null, 1999));
    }

    [Fact]
    public void NothingIsListedWithoutAQueryOrASeason()
    {
        Assert.Empty(Titles(null, null));
        Assert.Empty(Titles("   ", null));
    }

    [Fact]
    public void ResultsAreNewestSeasonFirstThenInCalendarOrder() =>
        Assert.Equal(["Blazor and .NET Aspire, together", "What's new in Blazor", "Source generators in practice", "Span<T> all the things"], Titles("a", null));

    [Fact]
    public void OnlyTheStartOfAVeryLongQueryIsRead()
    {
        var words = ArchiveSearch.Words(new string('a', 99) + " blazor");

        Assert.Equal([new string('a', 99)], words);
    }

    private static List<string> Titles(string? query, int? year) => [.. ArchiveSearch.Find(Posts, query, year).Select(p => p.Title)];
}
