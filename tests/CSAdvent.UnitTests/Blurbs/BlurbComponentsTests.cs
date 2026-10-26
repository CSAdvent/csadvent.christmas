using Bunit;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Contributors;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class BlurbComponentsTests : BunitContext
{
    public BlurbComponentsTests()
    {
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));
        Services.AddSingleton(Options.Create(new SiteOptions { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" }));
    }

    [Fact]
    public void AnAuthorCanCheckAPostThatHasntBeenLookedAt()
    {
        var box = RenderCheck(state: null);

        Assert.Contains("C# Advent blurb: not checked yet.", Text(box));
        var form = box.Find("form");
        Assert.Equal("my-spots/blurb-check", form.GetAttribute("action"));
        Assert.Equal("42", box.Find("input[name='SlotId']").GetAttribute("value"));
        Assert.Equal("my-spots", box.Find("input[name='ReturnUrl']").GetAttribute("value"));
        Assert.Equal("Check my post", box.Find("button").TextContent);
    }

    [Fact]
    public void BeforeTheirDayAMissingBlurbIsOnlyForTheAuthor()
    {
        var box = RenderCheck(State(BlurbStatus.Missing, "There's no link to csadvent.christmas near the top of the post."));

        Assert.Contains("C# Advent blurb: missing. There's no link to csadvent.christmas near the top of the post.", Text(box));
        Assert.Contains("Until your day starts, this is only for you.", Text(box));
        Assert.Contains("Last checked", Text(box));
    }

    [Fact]
    public void APageWeCouldntReadYetSaysWhyAndWhoToAsk()
    {
        var box = RenderCheck(State(BlurbStatus.Waiting, BlurbChecker.NotThePost));

        Assert.Contains("we couldn't read your post yet.", Text(box));
        Assert.Contains("The page may need JavaScript to show the post", Text(box));
        Assert.Contains("email hello@csadvent.christmas", Text(box));
    }

    [Theory]
    [InlineData(BlurbStatus.Passed, "C# Advent blurb: found. Thank you!")]
    [InlineData(BlurbStatus.Waived, "an organizer has OK'd your post")]
    public void APostThatPassedIsntCheckedAgain(BlurbStatus status, string note)
    {
        var box = RenderCheck(State(status, "Claude found the blurb."));

        Assert.Contains(note, Text(box));
        Assert.Empty(box.FindAll("form"));
    }

    [Fact]
    public void OnTheirDayAnUnsurePostIsTheOrganizersToDecide()
    {
        var box = RenderCheck(State(BlurbStatus.NeedsDecision, "Claude isn't sure."), revealed: true);

        Assert.Contains("C# Advent blurb: an organizer is taking a look.", Text(box));
    }

    [Theory]
    [InlineData("too-many", 42, "You've checked 10 times in the last hour")]
    [InlineData("paused", 42, "Checking on request is paused for now.")]
    [InlineData("checked", 42, "We've just looked at your post.")]
    [InlineData("checked", 7, null)]
    public void TheBoxSaysHowItsSpotsCheckWent(string outcome, int slot, string? message)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"my-spots?blurb={outcome}&slot={slot}");

        var box = RenderCheck(state: null);

        if (message is null)
        {
            Assert.Empty(box.FindAll("[role='status']"));
        }
        else
        {
            Assert.Contains(message, box.Find("[role='status']").TextContent);
        }
    }

    [Fact]
    public void TheOrganizersSeeTheBackupStandingByAndHowToKeepTheAuthor()
    {
        var details = new BlurbDetails(
            42,
            Applies: true,
            Enabled: true,
            HasPost: true,
            State(BlurbStatus.Missing, "There's no link to csadvent.christmas near the top of the post."),
            new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero),
            new BlurbBackup("Bo Backup", "Collection expressions everywhere", "https://example.com/collections"));

        var panel = Render<BlurbPanel>(p => p.Add(c => c.Details, details).Add(c => c.ReturnUrl, "manage/entries/42"));

        Assert.Equal("blurb", panel.Find("section").Id);
        Assert.Contains("Bo Backup is standing by", panel.Markup);
        Assert.Contains("To keep the author:", panel.Find("section").TextContent);
    }

    private IRenderedComponent<PostBlurbCheck> RenderCheck(BlurbState? state, bool revealed = false) =>
        Render<PostBlurbCheck>(p => p
            .Add(c => c.SlotId, 42)
            .Add(c => c.Year, 2026)
            .Add(c => c.IsRevealed, revealed)
            .Add(c => c.State, state)
            .Add(c => c.ReturnUrl, "my-spots"));

    private static BlurbState State(BlurbStatus status, string detail) =>
        new(status, detail, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false);

    private static string Text(IRenderedComponent<PostBlurbCheck> box) => box.Find("#blurb-42").TextContent;

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
