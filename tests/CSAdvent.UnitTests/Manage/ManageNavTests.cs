using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Manage;

public sealed class ManageNavTests
{
    [Theory]
    [InlineData("manage", "manage")]
    [InlineData("manage/entries", "manage/entries")]
    [InlineData("manage/entries/42", "manage/entries")]
    [InlineData("manage/social", "manage/social")]
    [InlineData("manage/social/signups", "manage/social/signups")]
    [InlineData("manage/authors/7", "manage/authors")]
    [InlineData("manage/authors/requests/3", "manage/authors/requests")]
    [InlineData("manage/authors/link-changes", "manage/authors/link-changes")]
    [InlineData("manage/authors/dead-links", "manage/authors/dead-links")]
    [InlineData("manage/emails/claim-received", "manage/emails")]
    [InlineData("manage/emails/queue", "manage/emails/queue")]
    public void TheCurrentLinkIsTheLongestOneThePageIsUnder(string path, string current) =>
        Assert.Equal(current, Assert.Single(ManageNav.Items(path, AttentionCounts.None), item => item.IsCurrent).Href);

    [Fact]
    public void ReachesThePagesThatWereOnlyLinkedFromOtherPages()
    {
        var hrefs = ManageNav.Items("manage", AttentionCounts.None).Select(item => item.Href).ToList();

        Assert.Contains("manage/authors/requests", hrefs);
        Assert.Contains("manage/authors/link-changes", hrefs);
        Assert.Contains("manage/authors/dead-links", hrefs);
        Assert.Contains("manage/social/signups", hrefs);
        Assert.Contains("manage/emails/queue", hrefs);
    }

    [Fact]
    public void CountsWhatsWaitingOnItsOwnPage()
    {
        var items = ManageNav.Items("manage", new AttentionCounts(Requests: 3, PostClaims: 1, LinkChanges: 2, FailedEmails: 4));

        Assert.Equal(
            [("manage/requests", 3, false), ("manage/authors/requests", 1, false), ("manage/authors/link-changes", 2, false), ("manage/emails/queue", 4, true)],
            items.Where(item => item.Count > 0).Select(item => (item.Href, item.Count, item.IsFailure)));
    }
}
