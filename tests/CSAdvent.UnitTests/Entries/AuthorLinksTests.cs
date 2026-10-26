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

    [Theory]
    [InlineData("https://twitter.com/madSimonJ/status/1181174805703385088")]
    [InlineData("https://x.com/ada/status/1181174805703385088?s=20")]
    [InlineData("https://mobile.twitter.com/ada/statuses/933356947377672192")]
    [InlineData("https://crosscuttingconcerns.com/The-Third-Annual-csharp-Advent#comment-4641190500")]
    public void WhereTheySignedUpIsNoWebsite(string signUp)
    {
        // The name falls back to their GitHub profile, or plain text, and their links don't call it a website (#166).
        Assert.Equal("https://github.com/ada", AuthorLinks.ForName(signUp, "ada"));
        Assert.Null(AuthorLinks.ForName(signUp, null));
        Assert.Equal([new AuthorLink("GitHub", "https://github.com/ada")], AuthorLinks.All(signUp, "ada", AuthorAccounts.None));
    }

    [Theory]
    [InlineData("https://twitter.com/ada")]
    [InlineData("https://crosscuttingconcerns.com")]
    [InlineData("https://ada.dev/blog/status/")]
    [InlineData("https://ada.dev/#contact")]
    public void ProfilesAndSitesAreStillTheirs(string url) => Assert.Equal(url, AuthorLinks.Website(url));
}
