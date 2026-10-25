using CSAdvent.UnitTests.Retrospectives;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;

namespace CSAdvent.UnitTests.Social;

public sealed class AnnouncementTests
{
    private static readonly AuthorAccounts Accounts = new("ada.bsky.social", "@ada@hachyderm.io", "https://www.linkedin.com/in/ada", "https://www.facebook.com/ada.dev", "ada_codes", "AdaCodes");

    private static readonly SocialAccountsOptions Ours = new() { Mastodon = "@csadvent@mastodon.social", Subreddit = "csadvent" };

    private static Announcement Entry(string title = "Pattern matching all the way down", AuthorAccounts? accounts = null) =>
        new(2026, 6, 2, 2, title, "Ada Lovelace", accounts ?? Accounts, "https://csadvent.christmas");

    [Theory]
    [InlineData(SocialPlatform.Bluesky, "#csharp #dotnet #csadvent")]
    [InlineData(SocialPlatform.Mastodon, "#csharp #dotnet #csadvent")]
    [InlineData(SocialPlatform.Facebook, "#csharp #csadvent")]
    [InlineData(SocialPlatform.X, "#csharp #dotnet #csadvent")]
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

        var linkedIn = AnnouncementFormatter.FormatSignUpsOpen(2026, SocialPlatform.LinkedIn, "https://csadvent.christmas", Ours);
        Assert.Equal($"https://www.linkedin.com/feed/?shareActive=true&text={Uri.EscapeDataString(linkedIn.Text)}", linkedIn.ComposerUrl);
        Assert.Contains("csadvent.christmas%2F%3Fvia%3Dlinkedin%26season%3D2026", linkedIn.ComposerUrl);

        var x = AnnouncementFormatter.FormatSignUpsOpen(2026, SocialPlatform.X, "https://csadvent.christmas", Ours);
        Assert.Equal("https://csadvent.christmas/?via=x&season=2026", x.Link);
        Assert.EndsWith("#csharp #dotnet #csadvent", x.Text);
        Assert.Equal($"https://x.com/intent/tweet?text={Uri.EscapeDataString(x.Text)}", x.ComposerUrl);
        Assert.True(x.Length <= AnnouncementFormatter.XLimit, $"X is {x.Length} long.");
    }

    [Fact]
    public void SeasonPostsFitOnX()
    {
        var posts = new[]
        {
            AnnouncementFormatter.FormatSignUpsSoon(2026, new DateOnly(2026, 11, 1), 30, SocialPlatform.X, "https://csadvent.christmas", Ours),
            AnnouncementFormatter.FormatRetrospective("C# Advent 2026: a look back", RetrospectiveDraftTests.Stats(), SocialPlatform.X, "https://csadvent.christmas", Ours),
        };

        Assert.All(posts, post => Assert.True(post.Length <= AnnouncementFormatter.XLimit, $"{post.Text} is {post.Length} long."));
    }

    [Fact]
    public void TheRetrospectivePostLinksToItsPageWithTheSeasonsNumbers()
    {
        var bluesky = AnnouncementFormatter.FormatRetrospective("C# Advent 2026: a look back", RetrospectiveDraftTests.Stats(), SocialPlatform.Bluesky, "https://csadvent.christmas", Ours);
        var reddit = AnnouncementFormatter.FormatRetrospective("C# Advent 2026: a look back", RetrospectiveDraftTests.Stats(), SocialPlatform.Reddit, "https://csadvent.christmas", Ours);
        var older = AnnouncementFormatter.FormatRetrospective("C# Advent 2019: a look back", RetrospectiveDraftTests.Stats(2019, traffic: false), SocialPlatform.Mastodon, "https://csadvent.christmas", Ours);

        Assert.StartsWith("Looking back at C# Advent 2026: 48 posts by 45 authors, read 12,345 times. Thank you to everyone who wrote and read them!", bluesky.Text);
        Assert.Equal("https://csadvent.christmas/archive/2026/retrospective?via=bluesky", bluesky.Link);
        Assert.True(bluesky.Length <= AnnouncementFormatter.BlueskyLimit);
        Assert.Equal("C# Advent 2026: a look back", reddit.Text);
        Assert.StartsWith("https://www.reddit.com/r/csadvent/submit?url=https%3A%2F%2Fcsadvent.christmas%2Farchive%2F2026%2Fretrospective%3Fvia%3Dreddit", reddit.ComposerUrl);
        Assert.StartsWith("Looking back at C# Advent 2019: 48 posts by 45 authors. Thank you", older.Text);
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
        Assert.Equal($"https://www.linkedin.com/feed/?shareActive=true&text={Uri.EscapeDataString(linkedIn.Text)}", linkedIn.ComposerUrl);
        Assert.Equal("Tag the author: https://www.linkedin.com/in/ada", linkedIn.TagHint);
        Assert.EndsWith("#csharp #csadvent", AnnouncementFormatter.Format(Entry(), SocialPlatform.Facebook).Text);

        var reddit = AnnouncementFormatter.Format(Entry(), SocialPlatform.Reddit, Ours);
        Assert.StartsWith("https://www.reddit.com/r/csadvent/submit?url=", reddit.ComposerUrl);
        Assert.Equal("“Pattern matching all the way down” by Ada Lovelace (C# Advent 2026, December 6, part 2)", reddit.Text);
        Assert.DoesNotContain("#", reddit.Text.Replace("C#", ""));
        Assert.Contains("url=https%3A%2F%2Fcsadvent.christmas%2F2026%2Fday%2F6%2F2%3Fvia%3Dreddit", reddit.ComposerUrl);
        Assert.Equal("Tag the author: u/ada_codes", reddit.TagHint);

        var x = AnnouncementFormatter.Format(Entry(), SocialPlatform.X);
        Assert.Contains("by Ada Lovelace (@AdaCodes)", x.Text);
        Assert.EndsWith("#csharp #dotnet #csadvent", x.Text);
        Assert.Equal($"https://x.com/intent/tweet?text={Uri.EscapeDataString(x.Text)}", x.ComposerUrl);
        Assert.Equal(AnnouncementFormatter.XLimit, x.Limit);
        Assert.Null(x.TagHint);
    }

    [Fact]
    public void ThePostsSummaryGoesInBeforeTheLink()
    {
        var announcement = Entry() with { Summary = "Switch expressions, list patterns and more." };

        foreach (var platform in new[] { SocialPlatform.Bluesky, SocialPlatform.Mastodon, SocialPlatform.LinkedIn, SocialPlatform.Facebook, SocialPlatform.X })
        {
            var formatted = AnnouncementFormatter.Format(announcement, platform, Ours);

            Assert.Contains($"\n\nSwitch expressions, list patterns and more.\n\n{formatted.Link}\n\n", formatted.Text);
            Assert.True(formatted.Limit is null || formatted.Length <= formatted.Limit, $"{platform} is {formatted.Length} long.");
        }

        // A Reddit post is a title and a link.
        Assert.Equal(
            "“Pattern matching all the way down” by Ada Lovelace (C# Advent 2026, December 6, part 2)",
            AnnouncementFormatter.Format(announcement, SocialPlatform.Reddit, Ours).Text);
    }

    [Fact]
    public void ASummaryThatDoesNotFitIsLeftOutRatherThanShorteningTheTitle()
    {
        var summary = string.Join(' ', Enumerable.Repeat("Spans everywhere.", 9))[..160];
        var announcement = Entry() with { Summary = summary };

        foreach (var platform in new[] { SocialPlatform.Bluesky, SocialPlatform.X })
        {
            var formatted = AnnouncementFormatter.Format(announcement, platform, Ours);

            Assert.DoesNotContain("Spans everywhere", formatted.Text);
            Assert.Contains("“Pattern matching all the way down”", formatted.Text);
            Assert.True(formatted.Length <= formatted.Limit, $"{platform} is {formatted.Length} long.");
        }

        Assert.Contains(summary, AnnouncementFormatter.Format(announcement, SocialPlatform.Mastodon, Ours).Text);
        Assert.Contains(summary, AnnouncementFormatter.Format(announcement, SocialPlatform.LinkedIn, Ours).Text);
    }

    [Fact]
    public void TheLinkCardDescribesThePostLikeItsPage()
    {
        var day = new AnnouncementDay(2026, 6, 2, IsRevealed: true, new DateTimeOffset(2026, 12, 6, 5, 0, 0, TimeSpan.Zero), []);
        var entry = new EntryAnnouncements(1, 2, "Patterns", "Ada Lovelace", [], new Dictionary<SocialPlatform, CSAdvent.Web.Data.SocialPost>());

        Assert.Equal("By Ada Lovelace, for C# Advent 2026: December 6, post 2.", SocialPostingService.Description(day, entry));
        Assert.Equal(
            "Switch expressions and more. By Ada Lovelace, for C# Advent 2026: December 6, post 2.",
            SocialPostingService.Description(day, entry with { Summary = "Switch expressions and more" }));
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

        foreach (var platform in new[] { SocialPlatform.Bluesky, SocialPlatform.Mastodon, SocialPlatform.Reddit, SocialPlatform.X })
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

    [Theory]
    [InlineData("https://csadvent.christmas/2026/day/6/2?via=x", 23)]
    [InlineData("Pattern matching, e.g. in C# 14.0", 33)]
    [InlineData("“Spans” – part 1", 16)]
    // An emoji or "…" counts twice.
    [InlineData("🎄", 2)]
    [InlineData("Interceptors…", 14)]
    // X links what looks like a domain, so it counts as a link.
    [InlineData("Minimal APIs in ASP.NET Core", 44)]
    [InlineData("A very long namespace: Microsoft.Extensions.DependencyInjection", 63)]
    public void XCountsLinksAndWideCharacters(string text, int length) =>
        Assert.Equal(length, AnnouncementFormatter.XLength(text));
}
