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

    [Fact]
    public void AnUnconfirmedPostAsksForACheckBeforeItsPostedAgain()
    {
        var card = RenderCard(SocialPlatform.Facebook, canPost: true, SeasonPostKind.SignUpsOpen, Unconfirmed(SeasonPostKind.SignUpsOpen), canPostAutomatically: true);

        Assert.Contains("Did it go out?", card.Find("article").TextContent);
        Assert.Contains("Facebook never said: Facebook didn't answer in time.", card.Find("article").TextContent);
        Assert.Contains("bg-amber-50", card.Find("article").GetAttribute("class"));
        Assert.Equal("Post now", card.Find("form[action='manage/social/signups/post'] button").TextContent.Trim());
        Assert.Equal("Mark as posted", card.Find("form[action='manage/social/signups/mark'] button").TextContent.Trim());
        Assert.Empty(card.FindAll("form[action='manage/social/signups/unmark']"));
    }

    [Fact]
    public void AComingSoonPostThatDidntGoOutCanBeDismissed()
    {
        var card = RenderCard(SocialPlatform.Facebook, canPost: true, SeasonPostKind.SignUpsSoon, Unconfirmed(SeasonPostKind.SignUpsSoon), canPostAutomatically: true);

        Assert.Equal("Dismiss", card.Find("form[action='manage/social/signups/unmark'] button").TextContent.Trim());
    }

    private static SeasonSocialPost Unconfirmed(SeasonPostKind kind) => new()
    {
        SeasonYear = 2026,
        Kind = kind,
        Platform = SocialPlatform.Facebook,
        Status = SocialPostStatus.Unconfirmed,
        At = new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero),
        MarkedBy = SocialPostingService.Automation,
        Error = "Facebook didn't answer in time.",
    };

    private IRenderedComponent<SeasonPostCard> RenderCard(
        SocialPlatform platform,
        bool canPost,
        SeasonPostKind kind = SeasonPostKind.SignUpsOpen,
        SeasonSocialPost? posted = null,
        bool canPostAutomatically = false) =>
        Render<SeasonPostCard>(p => p
            .Add(c => c.Year, 2026)
            .Add(c => c.Kind, kind)
            .Add(c => c.Post, new SeasonPostPlatform(
                kind is SeasonPostKind.SignUpsSoon
                    ? AnnouncementFormatter.FormatSignUpsSoon(2026, new DateOnly(2026, 11, 1), 8, platform, "https://csadvent.christmas", Ours)
                    : AnnouncementFormatter.FormatSignUpsOpen(2026, platform, "https://csadvent.christmas", Ours),
                new PlatformPosting(platform, SocialPostingMode.Manual, CanPostAutomatically: canPostAutomatically, IsDryRun: false, CheckedAt: null, CheckError: null),
                posted is null ? [] : [posted]))
            .Add(c => c.CanPost, canPost));
}
