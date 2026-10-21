using System.Security.Claims;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Claims;

namespace CSAdvent.UnitTests.Claims;

public sealed class SocialAccountsTests
{
    [Theory]
    [InlineData(SocialPlatform.Bluesky, "alice.bsky.social", "alice.bsky.social")]
    [InlineData(SocialPlatform.Bluesky, "@Alice.bsky.social", "alice.bsky.social")]
    [InlineData(SocialPlatform.Bluesky, "https://bsky.app/profile/alice.dev", "alice.dev")]
    [InlineData(SocialPlatform.Mastodon, "@alice@hachyderm.io", "@alice@hachyderm.io")]
    [InlineData(SocialPlatform.Mastodon, "alice@Hachyderm.io", "@alice@hachyderm.io")]
    [InlineData(SocialPlatform.Mastodon, "https://hachyderm.io/@alice", "@alice@hachyderm.io")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/in/alice-dev/", "https://www.linkedin.com/in/alice-dev")]
    [InlineData(SocialPlatform.LinkedIn, "linkedin.com/in/alice-dev", "https://www.linkedin.com/in/alice-dev")]
    [InlineData(SocialPlatform.LinkedIn, "alice-dev", "https://www.linkedin.com/in/alice-dev")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/alice.dev", "https://www.facebook.com/alice.dev")]
    [InlineData(SocialPlatform.Facebook, "fb.com/alice.dev", "https://www.facebook.com/alice.dev")]
    [InlineData(SocialPlatform.Facebook, "https://facebook.com/profile.php?id=100012345", "https://www.facebook.com/profile.php?id=100012345")]
    [InlineData(SocialPlatform.Reddit, "u/alice_dev", "alice_dev")]
    [InlineData(SocialPlatform.Reddit, "/u/alice_dev", "alice_dev")]
    [InlineData(SocialPlatform.Reddit, "https://www.reddit.com/user/alice_dev/", "alice_dev")]
    public void NormalizesWhatPeoplePaste(SocialPlatform platform, string input, string expected)
    {
        Assert.True(SocialAccounts.TryNormalize(platform, input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(SocialPlatform.Bluesky, "alice")]
    [InlineData(SocialPlatform.Bluesky, "https://twitter.com/alice")]
    [InlineData(SocialPlatform.Mastodon, "@alice")]
    [InlineData(SocialPlatform.Mastodon, "https://hachyderm.io/alice")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/company/acme")]
    [InlineData(SocialPlatform.Facebook, "https://twitter.com/alice")]
    [InlineData(SocialPlatform.Reddit, "u/a")]
    public void RejectsWhatIsntAnAccount(SocialPlatform platform, string input)
    {
        Assert.False(SocialAccounts.TryNormalize(platform, input, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsBlankAsNoAccount(string? input)
    {
        Assert.True(SocialAccounts.TryNormalize(SocialPlatform.Bluesky, input, out var normalized));
        Assert.Null(normalized);
    }
}

public sealed class DisplayNameTests
{
    private static ClaimsPrincipal User(string? name) =>
        new(new ClaimsIdentity(AdventClaims.For(1, "alice-dev", verifiedEmail: null, contributorId: 1, name), "test"));

    [Fact]
    public void UsesTheGitHubProfileName() => Assert.Equal("Alice Developer", User("Alice Developer").GetDisplayName());

    [Fact]
    public void FallsBackToTheLogin() => Assert.Equal("alice-dev", User(null).GetDisplayName());
}
