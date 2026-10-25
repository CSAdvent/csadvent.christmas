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
    [InlineData(SocialPlatform.X, "@Alice_Dev", "Alice_Dev")]
    [InlineData(SocialPlatform.X, "alice_dev", "alice_dev")]
    [InlineData(SocialPlatform.X, "https://x.com/alice_dev", "alice_dev")]
    [InlineData(SocialPlatform.X, "https://twitter.com/alice_dev/", "alice_dev")]
    [InlineData(SocialPlatform.X, "mobile.twitter.com/alice_dev", "alice_dev")]
    [InlineData(SocialPlatform.X, "https://x.com/alice_dev?s=21&t=abc", "alice_dev")]
    public void NormalizesWhatPeoplePaste(SocialPlatform platform, string input, string expected)
    {
        Assert.True(SocialAccounts.TryNormalize(platform, input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(SocialPlatform.Bluesky, "alice.bsky.social", "https://bsky.app/profile/alice.bsky.social")]
    [InlineData(SocialPlatform.Mastodon, "@alice@hachyderm.io", "https://hachyderm.io/@alice")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/in/alice-dev", "https://www.linkedin.com/in/alice-dev")]
    [InlineData(SocialPlatform.Facebook, "https://www.facebook.com/alice.dev", "https://www.facebook.com/alice.dev")]
    [InlineData(SocialPlatform.Reddit, "alice_dev", "https://www.reddit.com/user/alice_dev")]
    [InlineData(SocialPlatform.X, "alice_dev", "https://x.com/alice_dev")]
    public void AccountsLinkToTheirProfiles(SocialPlatform platform, string normalized, string url) =>
        Assert.Equal(url, SocialAccounts.ProfileUrl(platform, normalized));

    [Theory]
    [InlineData("linkedin", "https://linkedin.com/in/mgroves", SocialPlatform.LinkedIn, "https://www.linkedin.com/in/mgroves")]
    [InlineData("generic", "https://bsky.app/profile/alice.dev", SocialPlatform.Bluesky, "alice.dev")]
    [InlineData("mastodon", "https://hachyderm.io/@alice", SocialPlatform.Mastodon, "@alice@hachyderm.io")]
    [InlineData("facebook", "https://www.facebook.com/alice.dev", SocialPlatform.Facebook, "https://www.facebook.com/alice.dev")]
    [InlineData("reddit", "https://www.reddit.com/user/alice_dev", SocialPlatform.Reddit, "alice_dev")]
    [InlineData("twitter", "https://x.com/mgroves", SocialPlatform.X, "mgroves")]
    [InlineData("twitter", "https://twitter.com/CalvinAllen", SocialPlatform.X, "CalvinAllen")]
    [InlineData("linkedin", "https://www.linkedin.com/company/csadvent", null, null)]
    [InlineData("generic", "https://medium.com/@alice", null, null)]
    [InlineData("generic", "https://www.threads.net/@alice", null, null)]
    [InlineData("generic", "not a link", null, null)]
    public void TakesTheAccountsWeTagFromAGitHubProfile(string provider, string url, SocialPlatform? platform, string? account) =>
        Assert.Equal(platform is { } p ? (p, account!) : null, SocialAccounts.FromGitHub(provider, url));

    [Theory]
    [InlineData("https://twitter.com/alice", true)]
    [InlineData("https://x.com/alice", true)]
    [InlineData("https://bsky.app/profile/alice.dev", true)]
    [InlineData("https://hachyderm.io/@alice", true)]
    [InlineData("https://dotnet.social/@alice/", true)]
    [InlineData("https://www.linkedin.com/in/alice-dev", true)]
    [InlineData("https://m.facebook.com/alice.dev", true)]
    [InlineData("https://old.reddit.com/user/alice", true)]
    [InlineData("https://www.instagram.com/alice", true)]
    [InlineData("https://alice.dev", false)]
    [InlineData("https://alice.dev/blog/@archive", false)]
    [InlineData("https://github.com/alice", false)]
    [InlineData("https://medium.com/@alice", false)]
    [InlineData("https://alice.substack.com/@alice", false)]
    [InlineData("https://www.youtube.com/@alice", false)]
    [InlineData("https://notx.com/alice", false)]
    [InlineData(null, false)]
    public void TellsSocialProfilesFromWebsites(string? url, bool social) =>
        Assert.Equal(social, SocialAccounts.IsSocialProfile(url));

    [Theory]
    [InlineData(SocialPlatform.Bluesky, "alice")]
    [InlineData(SocialPlatform.Bluesky, "https://twitter.com/alice")]
    [InlineData(SocialPlatform.Mastodon, "@alice")]
    [InlineData(SocialPlatform.Mastodon, "https://hachyderm.io/alice")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/company/acme")]
    [InlineData(SocialPlatform.Facebook, "https://twitter.com/alice")]
    [InlineData(SocialPlatform.Reddit, "u/a")]
    [InlineData(SocialPlatform.X, "alice.bsky.social")]
    [InlineData(SocialPlatform.X, "@alice@hachyderm.io")]
    [InlineData(SocialPlatform.X, "a_name_much_too_long")]
    [InlineData(SocialPlatform.X, "https://x.com/alice/status/123")]
    [InlineData(SocialPlatform.X, "https://notx.com/alice")]
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
