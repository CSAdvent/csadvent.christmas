using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static CSAdvent.UnitTests.Manage.TodayFixtures;

namespace CSAdvent.UnitTests.Manage;

/// <summary>The Today panel on <c>/manage</c> (#171).</summary>
public sealed class TodayPanelTests : BunitContext
{
    public TodayPanelTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void CountsWhatsWaitingWithFailedEmailsInRed()
    {
        var panel = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(SampleSeason())));

        var tiles = panel.FindAll("ul[aria-label=\"What's waiting\"] a");
        Assert.Equal(
            [
                ("manage/requests", "2 Requests waiting"),
                ("manage/authors/requests", "1 Post claims waiting"),
                ("manage/authors/link-changes", "0 Link changes waiting"),
                ("manage/emails/queue", "12 Emails waiting to go"),
                ("manage/emails/queue", "1 Failed emails"),
            ],
            tiles.Select(t => (t.GetAttribute("href"), string.Join(' ', t.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))));
        Assert.Contains("bg-red-50", tiles[^1].ClassName);
        Assert.DoesNotContain("bg-red-50", tiles[0].ClassName);
    }

    [Fact]
    public void ShowsTodaysAndTomorrowsSpotsWithTheirPostsBlurbsAndAnnouncements()
    {
        var panel = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(SampleSeason())));

        Assert.Equal(["Today, December 6 · revealed", "Tomorrow, December 7 · revealed at midnight"], panel.FindAll("h4").Take(2).Select(h => Words(h.TextContent)));
        Assert.Equal("manage/social?year=2026&day=6", panel.FindAll("a").Single(a => a.TextContent == "December 6's announcements").GetAttribute("href"));

        var rows = panel.FindAll("tbody tr");
        Assert.Equal(4, rows.Count);
        Assert.Contains("Ada Lovelace", rows[0].TextContent);
        Assert.Equal("https://example.com/span", rows[0].QuerySelector("a[target='_blank']")!.GetAttribute("href"));
        Assert.Contains("Blurb passed", rows[0].TextContent);
        Assert.Equal(
            ["Bluesky: Posted automatically", "Mastodon: Automatic at 10 AM", "Facebook: Failed", "LinkedIn: To post by hand"],
            rows[0].QuerySelectorAll("td:last-child li").Select(li => li.TextContent.Trim()));

        // A failed post links to its card on the social page, where Retry is.
        var failed = rows[0].QuerySelector("td:last-child li a")!;
        Assert.Equal("manage/social?year=2026&day=6#announcement-41-Facebook-card", failed.GetAttribute("href"));
        Assert.Equal("401 Unauthorized", failed.GetAttribute("title"));

        Assert.Contains("Needs attention", rows[1].TextContent);
        Assert.Contains("No post yet", rows[1].TextContent);
        Assert.Equal("manage/entries/42", rows[1].QuerySelector("a")!.GetAttribute("href"));

        // Organizers see tomorrow's post here, as on the entries list.
        Assert.Contains(HiddenTitle, rows[2].TextContent);
        Assert.Contains("Not checked yet", rows[2].TextContent);
        Assert.Contains("LinkedIn: By hand, after the reveal", rows[2].TextContent);
        Assert.Contains("Open (dropped out)", rows[3].TextContent);
    }

    [Fact]
    public void ListsTheBackupsAndThePostsThatDidntGoOut()
    {
        var panel = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(SampleSeason(backups:
        [
            new(44, 3, 7, 2, "Barbara Liskov", false, Now.AddHours(-1), Now.AddHours(3)),
            new(42, 4, 6, 2, "Edsger Dijkstra", true, Now.AddHours(-2), Now.AddHours(-1)),
        ]))));

        var markup = Words(panel.Markup);
        var backups = panel.Find("h4 + ul").QuerySelectorAll("li").Select(li => Words(li.TextContent)).ToList();
        Assert.Equal(
            [
                "December 7, post 2: Barbara Liskov has been asked, and has until Dec 6, 2026, 11:00 AM Eastern to answer.",
                "December 6, post 2: Edsger Dijkstra is standing by to take the spot.",
            ],
            backups);

        var post = panel.FindAll("a").Single(a => a.TextContent.Contains("did it go out?", StringComparison.Ordinal));
        Assert.Equal("December 5, post 2 on X: did it go out?", post.TextContent.Trim());
        Assert.Equal("manage/social?year=2026&day=5#announcement-39-X-card", post.GetAttribute("href"));
        Assert.Contains("X didn't answer in time.", markup);
    }

    [Fact]
    public void ListsTheFirstFailedEmailsAndLinksToTheQueue()
    {
        var failed = Enumerable.Range(1, TodayPanel.FailedEmailsShown + 2).Select(id => Failed(id, "backup-ask-missing")).ToList();

        var panel = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(failedEmails: failed)));

        var items = panel.FindAll("h3 + ul li");
        Assert.Equal(TodayPanel.FailedEmailsShown, items.Count);
        Assert.Equal("Backup ask: no post link yet to reader-1@example.com: The email service answered 503", Words(items[0].TextContent));
        Assert.Contains("And 2 more.", panel.Markup);
        Assert.Contains(panel.FindAll("a"), a => a.GetAttribute("href") == "manage/emails/queue" && a.TextContent.Contains("Retry or discard", StringComparison.Ordinal));
    }

    [Fact]
    public void SaysWhenThereAreNoAdventDays()
    {
        var real = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(failedEmails: [])));
        Assert.Contains("No C# Advent days today or tomorrow.", real.Markup);
        Assert.Empty(real.FindAll("table"));
        Assert.DoesNotContain("Failed emails</h3>", real.Markup);

        // The test season's rehearsal is its own overview, with a way back.
        var rehearsal = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(failedEmails: [])).Add(c => c.Year, Season.TestYear));
        Assert.Contains("C# Advent 9000 has no days today or tomorrow.", rehearsal.Markup);
        Assert.Equal("manage", rehearsal.FindAll("a").Single(a => a.TextContent.Contains("Back to the real seasons", StringComparison.Ordinal)).GetAttribute("href"));
    }

    [Fact]
    public void TheTestSeasonSaysNothingIsPostedAutomatically()
    {
        var panel = Render<TodayPanel>(p => p.Add(c => c.Report, SampleReport(SampleSeason(year: Season.TestYear))).Add(c => c.Year, Season.TestYear));

        Assert.Equal("Test season (9000)", panel.Find("h3").TextContent);
        Assert.Contains("Nothing in the test season is posted automatically.", panel.Markup);
    }

    private static string Words(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
