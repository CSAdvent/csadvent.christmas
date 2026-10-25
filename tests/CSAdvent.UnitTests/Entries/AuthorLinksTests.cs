using CSAdvent.Web.Entries;
using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Entries;

public sealed class AuthorLinksTests
{
    [Theory]
    [InlineData("https://ada.dev", "ada", "https://ada.dev")]
    [InlineData(null, "ada", "https://github.com/ada")]
    [InlineData("https://twitter.com/ada", null, "https://twitter.com/ada")]
    [InlineData(null, null, null)]
    public void TheNameLinksToTheirWebsiteOrElseGitHub(string? website, string? login, string? expected) =>
        Assert.Equal(expected, AuthorLinks.ForName(website, login));

    [Fact]
    public void ListsTheirWebsiteGitHubAndAccounts()
    {
        var links = AuthorLinks.All("https://ada.dev", "ada", new AuthorAccounts("ada.bsky.social", "@ada@hachyderm.io", null, null, "ada_dev", "AdaCodes"));

        Assert.Equal(
            [
                new AuthorLink("Website", "https://ada.dev"),
                new AuthorLink("GitHub", "https://github.com/ada"),
                new AuthorLink("Bluesky", "https://bsky.app/profile/ada.bsky.social"),
                new AuthorLink("Mastodon", "https://hachyderm.io/@ada"),
                new AuthorLink("Reddit", "https://www.reddit.com/user/ada_dev"),
                new AuthorLink("X", "https://x.com/AdaCodes"),
            ],
            links);
    }

    [Fact]
    public void AnArchivedSocialLinkIsNotCalledAWebsite() =>
        Assert.Empty(AuthorLinks.All("https://twitter.com/ada", null, AuthorAccounts.None));
}
