using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class BlurbRulesTests
{
    [Theory]
    [InlineData(2025, false)]
    [InlineData(2026, true)]
    [InlineData(Season.TestYear, true)]
    public void TheRuleStartsIn2026AndCoversTheTestSeason(int year, bool applies) =>
        Assert.Equal(applies, BlurbRules.AppliesTo(year));

    [Theory]
    [InlineData("https://csadvent.christmas", true)]
    [InlineData("https://www.csadvent.christmas/2026", true)]
    [InlineData("http://CSADVENT.christmas/", true)]
    [InlineData("https://csadvent.christmas.example.com", false)]
    [InlineData("https://notcsadvent.christmas", false)]
    [InlineData("ftp://csadvent.christmas", false)]
    public void ALinkBackGoesToTheSite(string url, bool linksBack) =>
        Assert.Equal(linksBack, BlurbRules.IsLinkBack(url));

    [Fact]
    public void TheDeadlineIsTheAnnouncement()
    {
        var schedule = new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System);
        var posting = new SocialPostingOptions { PostAt = [new(10, 0), new(14, 0)] };

        Assert.Equal(new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero), BlurbRules.Deadline(schedule, posting, 2026, 6, 1));
        Assert.Equal(new DateTimeOffset(2026, 12, 6, 19, 0, 0, TimeSpan.Zero), BlurbRules.Deadline(schedule, posting, 2026, 6, 2));
    }

    [Fact]
    public void TheSuggestedBlurbComesInEachFormat()
    {
        var blurb = SuggestedBlurb.For(2026, slotsPerDay: 2);

        Assert.Equal("This post is part of the 2026 C# Advent calendar (https://csadvent.christmas): two C# posts a day, December 1–25.", blurb.Text);
        Assert.Equal("This post is part of the [2026 C# Advent calendar](https://csadvent.christmas): two C# posts a day, December 1–25.", blurb.Markdown);
        Assert.StartsWith("<p>This post is part of the <a href=\"https://csadvent.christmas\">2026 C# Advent calendar</a>: two C# posts a day", blurb.Html);
        Assert.Contains("a C# post a day", SuggestedBlurb.For(2017, slotsPerDay: 1).Text);
    }

    [Fact]
    public void PostsAreOnlyCheckedWithAnApiKeyOrInADryRun()
    {
        Assert.False(new BlurbOptions().IsEnabled);
        Assert.True(new BlurbOptions { DryRun = true }.IsEnabled);
        Assert.True(new BlurbOptions { ApiKey = "sk-ant-test" }.IsEnabled);
        Assert.Equal("claude-haiku-5-5", new BlurbOptions().Model);
    }
}
