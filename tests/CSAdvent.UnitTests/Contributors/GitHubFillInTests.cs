using CSAdvent.Web.Claims;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Contributors;

public sealed class GitHubFillInTests
{
    private static readonly GitHubSocialAccount[] Matt =
    [
        new("twitter", "https://x.com/mgroves"),
        new("linkedin", "https://linkedin.com/in/mgroves"),
        new("generic", "https://bsky.app/profile/mgroves.com"),
    ];

    [Fact]
    public void FillsInTheAccountsWeTag()
    {
        var contributor = new Contributor { GithubLogin = "mgroves" };

        var added = ProfileService.FillIn(contributor, Matt);

        Assert.Equal([SocialPlatform.LinkedIn, SocialPlatform.Bluesky], added);
        Assert.Equal(("https://www.linkedin.com/in/mgroves", "mgroves.com"), (contributor.LinkedInUrl, contributor.BlueskyHandle));
    }

    [Fact]
    public void NeverReplacesAnAccountTheyGaveUs()
    {
        var contributor = new Contributor { GithubLogin = "mgroves", BlueskyHandle = "matt.bsky.social" };

        var added = ProfileService.FillIn(contributor, Matt);

        Assert.Equal([SocialPlatform.LinkedIn], added);
        Assert.Equal("matt.bsky.social", contributor.BlueskyHandle);
    }

    [Fact]
    public void TakesTheFirstOfTwoAccountsOnOnePlatform()
    {
        var contributor = new Contributor { GithubLogin = "ada" };

        ProfileService.FillIn(contributor, [new("mastodon", "https://hachyderm.io/@ada"), new("mastodon", "https://dotnet.social/@ada")]);

        Assert.Equal("@ada@hachyderm.io", contributor.MastodonHandle);
    }
}
