using CSAdvent.Web.Claims;
using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Social;

public sealed class AnnouncementTests
{
    private static readonly AuthorAccounts Accounts = new("ada.bsky.social", "@ada@hachyderm.io", "https://www.linkedin.com/in/ada", "https://www.facebook.com/ada.dev", "ada_codes");

    private static readonly SocialAccountsOptions Ours = new() { Mastodon = "@csadvent@mastodon.social", Subreddit = "csadvent" };

    private static Announcement Entry(string title = "Pattern matching all the way down", AuthorAccounts? accounts = null) =>
        new(2026, 6, 2, 2, title, "Ada Lovelace", accounts ?? Accounts, "https://csadvent.christmas");

    [Theory]
    [InlineData(SocialPlatform.Bluesky, "#csharp #dotnet #csadvent")]
    [InlineData(SocialPlatform.Mastodon, "#csharp #dotnet #csadvent")]
    [InlineData(SocialPlatform.Facebook, "#csharp #csadvent")]
    public void ATestPostIsFormattedLikeAnAnnouncementButAboutNoEntry(SocialPlatform platform, string hashtags)
    {
        var formatted = AnnouncementFormatter.Format(Announcement.Test(2026, "https://csadvent.christmas", "abc123def456"), platform, Ours);
        var link = $"https://csadvent.christmas/test-post/abc123def456?via={platform.ToString().ToLowerInvariant()}";

        Assert.Equal(
            $"C# Advent test post (please ignore): “Checking that C# Advent can post here automatically” by the C# Advent organizers\n\n{link}\n\n{hashtags}",
            formatted.Text);
        Assert.Equal(link, formatted.Link);
    }

    [Fact]
    public void TheSignUpsPostLinksToAFreshAddressForTheCalendar()
    {
        var bluesky = AnnouncementFormatter.FormatSignUpsOpen(2026, SocialPlatform.Bluesky, "https://csadvent.christmas", Ours);
        var facebook = AnnouncementFormatter.FormatSignUpsOpen(2026, SocialPlatform.Facebook, "https://csadvent.christmas", Ours);
        var reddit = AnnouncementFormatter.FormatSignUpsOpen(2026, SocialPlatform.Reddit, "https://csadvent.christmas", Ours);

        Assert.StartsWith("Sign-ups for C# Advent 2026 are open! Claim a day in December", bluesky.Text);
        Assert.Equal("https://csadvent.christmas/?via=bluesky&season=2026", bluesky.Link);
        Assert.Contains(bluesky.Link, bluesky.Text);
        Assert.True(bluesky.Length <= AnnouncementFormatter.BlueskyLimit);
        Assert.EndsWith("#csharp #csadvent", facebook.Text);
        Assert.Equal("Sign-ups for C# Advent 2026 are open: claim a day in December and write a post about C#", reddit.Text);
        Assert.StartsWith("https://www.reddit.com/r/csadvent/submit?url=", reddit.ComposerUrl);
    }

    [Theory]
    [InlineData(8, "C# Advent 2026 sign-ups open in 8 days, on November 1!", "&in=8")]
    [InlineData(1, "C# Advent 2026 sign-ups open tomorrow, November 1!", "&in=1")]
    public void TheComingSoonPostCountsDown(int days, string start, string linkEnd)
    {
        var mastodon = AnnouncementFormatter.FormatSignUpsSoon(2026, new DateOnly(2026, 11, 1), days, SocialPlatform.Mastodon, "https://csadvent.christmas", Ours);

        Assert.StartsWith(start, mastodon.Text);
        Assert.EndsWith(linkEnd, mastodon.Link);
        Assert.True(mastodon.Length <= AnnouncementFormatter.MastodonLimit);
        Assert.StartsWith("https://mastodon.social/share?text=", mastodon.ComposerUrl);
    }

    [Fact]
    public void EveryPlatformSaysTheSameThingAndLinksThroughUs()
    {
        foreach (var platform in Enum.GetValues<SocialPlatform>())
        {
            var formatted = AnnouncementFormatter.Format(Entry(), platform, Ours);
            var tag = platform.ToString().ToLowerInvariant();

            Assert.Contains("Pattern matching all the way down", formatted.Text);
            Assert.Contains("Ada Lovelace", formatted.Text);
            Assert.Contains("C# Advent 2026, December 6, part 2", formatted.Text);
            Assert.Equal($"https://csadvent.christmas/2026/day/6/2?via={tag}", formatted.Link);
            if (platform is not SocialPlatform.Reddit)
            {
                Assert.Contains(formatted.Link, formatted.Text);
            }

            Assert.True(formatted.Limit is null || formatted.Length <= formatted.Limit, $"{platform} is {formatted.Length} long.");
        }
    }

    [Fact]
    public void EachPlatformGetsItsOwnConventions()
    {
        var bluesky = AnnouncementFormatter.Format(Entry(), SocialPlatform.Bluesky);
        Assert.Contains("(@ada.bsky.social)", bluesky.Text);
        Assert.EndsWith("#csharp #dotnet #csadvent", bluesky.Text);
        Assert.StartsWith("https://bsky.app/intent/compose?text=", bluesky.ComposerUrl);

        var mastodon = AnnouncementFormatter.Format(Entry(), SocialPlatform.Mastodon, Ours);
        Assert.Contains("(@ada@hachyderm.io)", mastodon.Text);
        Assert.StartsWith("https://mastodon.social/share?text=", mastodon.ComposerUrl);
        Assert.Null(AnnouncementFormatter.Format(Entry(), SocialPlatform.Mastodon).ComposerUrl);

        // Pasted text can't tag on LinkedIn, Facebook or Reddit, so the page says who to tag.
        var linkedIn = AnnouncementFormatter.Format(Entry(), SocialPlatform.LinkedIn);
        Assert.Null(linkedIn.ComposerUrl);
        Assert.Equal("Tag the author: https://www.linkedin.com/in/ada", linkedIn.TagHint);
        Assert.EndsWith("#csharp #csadvent", AnnouncementFormatter.Format(Entry(), SocialPlatform.Facebook).Text);

        var reddit = AnnouncementFormatter.Format(Entry(), SocialPlatform.Reddit, Ours);
        Assert.StartsWith("https://www.reddit.com/r/csadvent/submit?url=", reddit.ComposerUrl);
        Assert.Equal("“Pattern matching all the way down” by Ada Lovelace (C# Advent 2026, December 6, part 2)", reddit.Text);
        Assert.DoesNotContain("#", reddit.Text.Replace("C#", ""));
        Assert.Contains("url=https%3A%2F%2Fcsadvent.christmas%2F2026%2Fday%2F6%2F2%3Fvia%3Dreddit", reddit.ComposerUrl);
        Assert.Equal("Tag the author: u/ada_codes", reddit.TagHint);
    }

    [Fact]
    public void AuthorsWithoutAccountsAreSimplyNamed()
    {
        var bluesky = AnnouncementFormatter.Format(Entry(accounts: AuthorAccounts.None), SocialPlatform.Bluesky);

        Assert.Contains("by Ada Lovelace\n", bluesky.Text);
        Assert.Null(AnnouncementFormatter.Format(Entry(accounts: AuthorAccounts.None), SocialPlatform.LinkedIn).TagHint);
    }

    [Fact]
    public void LongTitlesAreShortenedToFitEachLimit()
    {
        var title = string.Join(' ', Enumerable.Repeat("source generators and interceptors", 20));

        foreach (var platform in new[] { SocialPlatform.Bluesky, SocialPlatform.Mastodon, SocialPlatform.Reddit })
        {
            var formatted = AnnouncementFormatter.Format(Entry(title), platform);
            Assert.True(formatted.Length <= formatted.Limit, $"{platform} is {formatted.Length} long.");
            Assert.Contains("…”", formatted.Text);
            Assert.Contains("Ada Lovelace", formatted.Text);
        }
    }

    [Fact]
    public void PlatformsCountTheirOwnWay()
    {
        Assert.Equal(1, AnnouncementFormatter.Graphemes("🎄"));
        Assert.Equal(23, AnnouncementFormatter.MastodonLength("https://csadvent.christmas/2026/day/6/2?via=mastodon"));
        Assert.Equal(4, AnnouncementFormatter.MastodonLength("@ada@hachyderm.io"));
        Assert.Equal(("mastodon.social", "csadvent"), (Ours.MastodonServer, Ours.MastodonUser));
        Assert.Null(new SocialAccountsOptions().MastodonServer);
    }
}
