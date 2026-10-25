using CSAdvent.Web.Authors;
using CSAdvent.Web.Entries;

namespace CSAdvent.UnitTests.Authors;

public sealed class AuthorPagesTests
{
    private static RevealedPost Post(int year, int day, string author, string? login = null, int? contributorId = null) =>
        new(year * 100 + day, year, day, 1, 2, $"A {year} post", null, author, null, contributorId ?? (login is null ? null : 1), login);

    [Fact]
    public void LinkedAuthorsAreFoundByLoginAndArchiveAuthorsByName()
    {
        Assert.Equal("authors/CalvinAllen", AuthorPageLinks.For("CalvinAllen", "Calvin A. Allen"));
        Assert.Equal("authors/name/ada%20lovelace", AuthorPageLinks.For(null, "Ada  Lovelace"));
        Assert.Equal("authors/name/g%C3%A9rald%20barr%C3%A9", AuthorPageLinks.ForName("Gérald Barré"));
        Assert.Equal("authors/name/steve%20%22ardalis%22%20smith", AuthorPageLinks.ForName("Steve \"ardalis\" Smith"));
    }

    [Fact]
    public void TheDirectoryHasEachAuthorOnceWithTheirLatestName()
    {
        var authors = AuthorDirectory.List(
        [
            Post(2024, 3, "Calvin A. Allen", "CalvinAllen", 7),
            Post(2019, 5, "Calvin Allen", "calvinallen", 7),
            Post(2018, 2, "Ada Lovelace"),
            Post(2022, 9, "ada  lovelace"),
            Post(2023, 4, "Bob"),
            Post(2021, 1, "Ágnes"),
        ]);

        // Sorted by name the way people read them, so "Ágnes" is with the A's.
        Assert.Equal(["ada  lovelace", "Ágnes", "Bob", "Calvin A. Allen"], authors.Select(a => a.Name));
        var calvin = authors.Single(a => a.Name == "Calvin A. Allen");
        Assert.Equal(("authors/CalvinAllen", 2, "2019–2024"), (calvin.Page, calvin.Posts, calvin.Years));
        Assert.Equal(2024, calvin.Latest.Year);
        var ada = authors[0];
        Assert.Equal(("authors/name/ada%20lovelace", 2, "2018–2022"), (ada.Page, ada.Posts, ada.Years));
        Assert.Equal("2023", authors.Single(a => a.Name == "Bob").Years);
    }

    [Fact]
    public void AnAuthorLinkedLaterIsNotAlsoListedByName()
    {
        // The organizers link the archive's "Ada Lovelace" to a GitHub account; any of her posts left unlinked still show by name.
        var authors = AuthorDirectory.List([Post(2018, 2, "Ada Lovelace", "ada", 1), Post(2019, 2, "Ada Lovelace")]);

        Assert.Equal(["authors/ada", "authors/name/ada%20lovelace"], authors.Select(a => a.Page).Order());
    }

    [Theory]
    [InlineData(new[] { 2023 }, "1 post, for C# Advent 2023.", "1 post, for C# Advent 2023, by Ada Lovelace.")]
    [InlineData(new[] { 2023, 2023 }, "2 posts, for C# Advent 2023.", "2 posts, for C# Advent 2023, by Ada Lovelace.")]
    [InlineData(new[] { 2024, 2020, 2018 }, "3 posts for C# Advent, from 2018 to 2024.", "3 posts for C# Advent, from 2018 to 2024, by Ada Lovelace.")]
    public void APageSaysHowManyPostsAndWhen(int[] years, string summary, string description)
    {
        var page = new AuthorPageView("Ada Lovelace", "authors/name/ada%20lovelace", [], [.. years.Select((year, i) => Post(year, i + 1, "Ada Lovelace"))]);

        Assert.Equal(summary, page.Summary);
        Assert.Equal(description, page.Description);
    }
}
