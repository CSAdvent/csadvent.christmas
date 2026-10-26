using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using CSAdvent.Web.DeadLinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class LinkCheckCardTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2027, 1, 10, 17, 0, 0, TimeSpan.Zero);

    public LinkCheckCardTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void ADeadPostSaysWhereReadersGoAndOffersEveryFix()
    {
        var card = Render(Row(LinkStatus.Dead, archivedUrl: "https://web.archive.org/web/2018/https://blog.example.com/post"), done: null);

        var text = Text(card);
        var badge = card.Find("span.rounded-full");
        Assert.Equal("Dead", badge.TextContent);
        Assert.Equal("The page answered 404 Not Found.", badge.NextElementSibling!.TextContent);
        Assert.Contains("Dead since Jan 7, 2027", text);
        Assert.Contains("Saved copy (found at the Wayback Machine):", text);
        Assert.Equal("https://web.archive.org/web/2018/https://blog.example.com/post", card.Find("a[href^='https://web.archive.org']").TextContent);
        Assert.Contains("Readers who follow its link get this copy.", text);

        Assert.Equal(
            ["manage/links/check", "manage/links/working", "manage/links/dead", "manage/links/lookup", "manage/links/replace", "manage/links/archive"],
            card.FindAll("form").Select(f => f.GetAttribute("action")));
        Assert.All(card.FindAll("input[name='SlotId']"), input => Assert.Equal("42", input.GetAttribute("value")));
        Assert.All(card.FindAll("input[name='ReturnUrl']"), input => Assert.Equal("manage/authors/dead-links#post-42", input.GetAttribute("value")));
        Assert.Equal("7", card.Find("form[action='manage/links/replace'] input[name='Version']").GetAttribute("value"));
        Assert.Equal("https://web.archive.org/web/2018/https://blog.example.com/post", card.Find("#archive-42").GetAttribute("value"));
    }

    [Fact]
    public void ADecisionStandsUntilSomeoneChecks()
    {
        var card = Render(Row(LinkStatus.Working) with { DecidedBy = "CalvinAllen", Detail = "CalvinAllen said it works." }, done: "working");

        Assert.Contains("CalvinAllen decided, so it isn't checked again until its link changes or someone presses Check now.", Text(card));
        Assert.Equal("Marked as working.", card.Find("[role=status]").TextContent);
        Assert.Empty(card.FindAll("form[action='manage/links/working']"));
    }

    [Fact]
    public void AnInvalidAddressIsAnError()
    {
        var card = Render(Row(LinkStatus.Failing), done: "invalid");

        Assert.Contains("isn't a full web address", card.Find("[role=alert]").TextContent);
        Assert.Contains("Failing since Jan 9, 2027", Text(card));
    }

    [Fact]
    public void ADeadPostWithoutACopyStillGoesToItsAddress()
    {
        var card = Render(Row(LinkStatus.Dead) with { ArchiveCheckedAt = Now }, done: null);

        Assert.Contains("The Wayback Machine had no saved copy when it was last asked", Text(card));
        Assert.Contains("readers still get the original address", Text(card));
    }

    private IRenderedComponent<LinkCheckCard> Render(LinkCheckRow row, string? done) =>
        Render<LinkCheckCard>(p => p
            .Add(c => c.Row, row)
            .Add(c => c.ShowPost, true)
            .Add(c => c.ReturnUrl, "manage/authors/dead-links#post-42")
            .Add(c => c.Done, done));

    private static LinkCheckRow Row(LinkStatus status, string? archivedUrl = null) => new(
        42, 2018, 5, 1, 2, "Span<T> all the way down", "Ada", "https://blog.example.com/post", 7, status, "The page answered 404 Not Found.",
        Failures: status == LinkStatus.Dead ? 3 : 1,
        FailingSince: Now.AddDays(-1),
        DeadSince: status == LinkStatus.Dead ? Now.AddDays(-3) : null,
        CheckedAt: Now,
        NextCheckAt: Now.AddDays(30),
        ArchivedUrl: archivedUrl,
        ArchivedBy: null,
        ArchiveCheckedAt: archivedUrl is null ? null : Now,
        DecidedBy: null);

    private static string Text(IRenderedComponent<LinkCheckCard> card) =>
        System.Text.RegularExpressions.Regex.Replace(card.Find("div").TextContent, @"\s+", " ");
}
