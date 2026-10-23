using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Social;

public sealed class SocialPostingPanelTests : BunitContext
{
    public SocialPostingPanelTests()
    {
        Services.AddSingleton(Options.Create(new SocialPostingOptions { PostAt = [new(10, 0), new(14, 0)] }));
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));
    }

    [Fact]
    public void EachPlatformCanBeSetAndCheckedWithoutASeason()
    {
        var panel = Render<SocialPostingPanel>(p => p
            .Add(c => c.Platforms,
            [
                new PlatformPosting(SocialPlatform.Bluesky, SocialPostingMode.Automatic, CanPostAutomatically: true, IsDryRun: false, CheckedAt: null, CheckError: null),
                new PlatformPosting(SocialPlatform.Mastodon, SocialPostingMode.Manual, CanPostAutomatically: false, IsDryRun: false, CheckedAt: null, CheckError: null),
                new PlatformPosting(SocialPlatform.LinkedIn, SocialPostingMode.Manual, CanPostAutomatically: false, IsDryRun: false, CheckedAt: null, CheckError: null),
            ])
            .Add(c => c.ReturnUrl, "manage/social"));

        Assert.Contains("at 10 AM (post 1) and 2 PM (post 2) Eastern", panel.Markup);
        Assert.Equal(3, panel.FindAll("form[action='manage/social/mode']").Count);
        Assert.All(panel.FindAll("input[name='ReturnUrl']"), input => Assert.Equal("manage/social", input.GetAttribute("value")));

        // Only a platform that can post automatically offers Automatic, and has credentials to check.
        var check = Assert.Single(panel.FindAll("form[action='manage/social/check']"));
        Assert.Equal("Bluesky", check.QuerySelector("input[name='Platform']")!.GetAttribute("value"));
        Assert.Contains("No credentials, so it's copy and paste.", panel.Markup);
        Assert.Contains("Copy and paste only: there's no API access.", panel.Markup);
        Assert.Equal("Bluesky", Assert.Single(panel.FindAll("form[action='manage/social/test']")).QuerySelector("input[name='Platform']")!.GetAttribute("value"));
    }

    [Fact]
    public void TheLastTestPostIsLinked()
    {
        var panel = Render<SocialPostingPanel>(p => p
            .Add(c => c.Platforms,
            [
                new PlatformPosting(SocialPlatform.Mastodon, SocialPostingMode.Manual, CanPostAutomatically: true, IsDryRun: false, CheckedAt: null, CheckError: null,
                    TestedAt: new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero), TestPostUrl: "https://mastodon.social/@csadvent/1"),
            ])
            .Add(c => c.ReturnUrl, "manage/social"));

        Assert.Contains("Test post sent", panel.Markup);
        Assert.Equal("https://mastodon.social/@csadvent/1", panel.Find("a[target='_blank']").GetAttribute("href"));
    }
}
