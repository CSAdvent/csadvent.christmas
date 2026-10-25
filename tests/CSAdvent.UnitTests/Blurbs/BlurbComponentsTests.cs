using Bunit;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class BlurbComponentsTests : BunitContext
{
    public BlurbComponentsTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void AnEntryShowsItsBlurbClaudesAnswerAndTheButtons()
    {
        var details = new BlurbDetails(
            42,
            Applies: true,
            Enabled: true,
            HasPost: true,
            new BlurbState(
                BlurbStatus.NeedsDecision,
                "Claude found the blurb, but its quote of it isn't in the post.",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                false,
                Quote: "This post is part of the 2026 C# Advent calendar",
                Reason: "It opens with the blurb."),
            new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero));

        var panel = Render<BlurbPanel>(p => p.Add(c => c.Details, details).Add(c => c.ReturnUrl, "manage/entries/42"));

        Assert.Contains("Needs a decision", panel.Markup);
        Assert.Contains("Claude's answer", panel.Find("section").TextContent);
        Assert.Contains("This post is part of the 2026 C# Advent calendar", panel.Find("blockquote").TextContent);
        Assert.Contains("It opens with the blurb.", panel.Markup);
        Assert.Equal(
            ["manage/blurbs/check", "manage/blurbs/decide", "manage/blurbs/decide", "manage/blurbs/decide", "manage/blurbs/backup"],
            panel.FindAll("form").Select(f => f.GetAttribute("action")));
        Assert.Equal(
            ["Passed", "Missing", "AnnounceAnyway"],
            panel.FindAll("input[name='Decision']").Select(i => i.GetAttribute("value")));
        Assert.All(panel.FindAll("input[name='SlotId']"), input => Assert.Equal("42", input.GetAttribute("value")));
    }

    [Fact]
    public void ANoteSaysWhenTheCheckIsOff()
    {
        var panel = Render<BlurbPanel>(p => p
            .Add(c => c.Details, new BlurbDetails(42, true, Enabled: false, true, null, DateTimeOffset.UtcNow))
            .Add(c => c.ReturnUrl, "manage/entries/42"));

        Assert.Contains("there's no Anthropic API key", panel.Find("section").TextContent);
        Assert.Empty(panel.FindAll("form"));
    }

    [Fact]
    public void SeasonsBefore2026HaveNoBlurbPanel()
    {
        var panel = Render<BlurbPanel>(p => p
            .Add(c => c.Details, new BlurbDetails(42, Applies: false, true, true, null, DateTimeOffset.UtcNow))
            .Add(c => c.ReturnUrl, "manage/entries/42"));

        Assert.Empty(panel.Markup.Trim());
    }
}
