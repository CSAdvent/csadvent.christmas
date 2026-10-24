using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Social;

public sealed class SeasonPostCardTests : BunitContext
{
    private static readonly SocialAccountsOptions Ours = new() { Subreddit = "csadvent" };

    public SeasonPostCardTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void TheComposerIsShownButDisabledUntilItCanBePosted()
    {
        var card = RenderCard(SocialPlatform.Reddit, canPost: false);

        Assert.Empty(card.FindAll("a[href^='https://www.reddit.com/']"));
        Assert.Equal("Open composer", card.Find("span[aria-disabled='true']").TextContent);
        Assert.Contains("Opens once sign-ups open", card.Markup);
    }

    [Theory]
    [InlineData(SocialPlatform.Reddit, "https://www.reddit.com/r/csadvent/submit?url=")]
    [InlineData(SocialPlatform.LinkedIn, "https://www.linkedin.com/feed/?shareActive=true&text=")]
    public void TheComposerOpensOnceItCanBePosted(SocialPlatform platform, string composer)
    {
        var card = RenderCard(platform, canPost: true);

        Assert.StartsWith(composer, card.Find("a[target='_blank']").GetAttribute("href"));
        Assert.Empty(card.FindAll("span[aria-disabled='true']"));
    }

    private IRenderedComponent<SeasonPostCard> RenderCard(SocialPlatform platform, bool canPost) =>
        Render<SeasonPostCard>(p => p
            .Add(c => c.Year, 2026)
            .Add(c => c.Kind, SeasonPostKind.SignUpsOpen)
            .Add(c => c.Post, new SeasonPostPlatform(
                AnnouncementFormatter.FormatSignUpsOpen(2026, platform, "https://csadvent.christmas", Ours),
                new PlatformPosting(platform, SocialPostingMode.Manual, CanPostAutomatically: false, IsDryRun: false, CheckedAt: null, CheckError: null),
                []))
            .Add(c => c.CanPost, canPost));
}
