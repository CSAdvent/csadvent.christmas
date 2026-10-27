using CSAdvent.Web.Data;
using CSAdvent.Web.DeadLinks;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class LinkCheckAuthorTests
{
    [Fact]
    public void AuthorsAreInOrderOfNameAndTheirPostsOldestFirst()
    {
        var authors = LinkCheckAuthor.Group(
        [
            Row(1, 2024, 3, 1, "zoe"),
            Row(2, 2019, 7, 2, "Ada"),
            Row(3, 2024, 1, 2, "Bea"),
            Row(4, 2017, 9, 1, "Ada"),
            Row(5, 2019, 7, 1, "Ada"),
        ]);

        Assert.Equal(["Ada", "Bea", "zoe"], authors.Select(a => a.Name));
        Assert.Equal([4, 5, 2], authors[0].Posts.Select(p => p.SlotId));
    }

    [Fact]
    public void ALinkedAuthorIsOneAuthorWhateverTheirNames()
    {
        var author = Assert.Single(LinkCheckAuthor.Group(
        [
            Row(1, 2020, 1, 1, "Calvin Allen", contributorId: 7, email: "calvin@example.com"),
            Row(2, 2023, 1, 1, "Calvin A. Allen", contributorId: 7, email: "CALVIN@example.com"),
            Row(3, 2018, 1, 1, "C. Allen", contributorId: 7, email: "c.allen@example.com"),
        ]));

        Assert.Equal((7, (string?)null, "Calvin A. Allen"), (author.ContributorId, author.Key, author.Name));
        Assert.Equal(["c.allen@example.com", "calvin@example.com"], author.Emails);
        Assert.Equal("manage/authors/7", author.Profile);
    }

    [Fact]
    public void UnlinkedAuthorsAreGroupedByNameApartFromLinkedOnes()
    {
        var authors = LinkCheckAuthor.Group(
        [
            Row(1, 2018, 1, 1, "Ada  Lovelace"),
            Row(2, 2019, 1, 1, "ada lovelace", email: "ada@example.com"),
            Row(3, 2021, 1, 1, "Ada Lovelace", contributorId: 3),
        ]);

        Assert.Equal(2, authors.Count);
        var unlinked = Assert.Single(authors, a => a.ContributorId is null);
        Assert.Equal(("ada lovelace", "ada lovelace"), (unlinked.Key, unlinked.Name));
        Assert.Equal([1, 2], unlinked.Posts.Select(p => p.SlotId));
        Assert.Equal(["ada@example.com"], unlinked.Emails);
        Assert.Equal("manage/authors/unlinked?key=ada%20lovelace", unlinked.Profile);
        Assert.Empty(Assert.Single(authors, a => a.ContributorId == 3).Emails);
    }

    private static LinkCheckRow Row(int slotId, int year, int day, int part, string author, int? contributorId = null, string? email = null) => new(
        slotId, year, day, part, 2, $"{author}'s post", author, contributorId, email, $"https://example.com/{slotId}", 1, LinkStatus.Dead, null,
        Failures: 3,
        FailingSince: null,
        DeadSince: null,
        CheckedAt: null,
        NextCheckAt: DateTimeOffset.UnixEpoch,
        ArchivedUrl: null,
        ArchivedBy: null,
        ArchiveCheckedAt: null,
        DecidedBy: null);
}
