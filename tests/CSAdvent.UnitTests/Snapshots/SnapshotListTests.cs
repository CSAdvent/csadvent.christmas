using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using CSAdvent.Web.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Snapshots;

public sealed class SnapshotListTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 12, 3, 17, 0, 0, TimeSpan.Zero);

    public SnapshotListTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void APostWithoutSnapshotsSaysSo() =>
        Assert.Contains("None yet.", Text(Render([])));

    [Fact]
    public void EachSnapshotSaysWhereItStands()
    {
        var list = Render(
        [
            new(SnapshotMoment.Live, SnapshotStatus.Saved, 0, Now, Now, "https://web.archive.org/web/20261203170000/https://blog.example.com/post", Now, null),
            new(SnapshotMoment.AfterSeason, SnapshotStatus.Failed, 5, Now, Now, null, null, "The Wayback Machine said: Not found. (error:not-found)"),
        ]);

        Assert.Equal(["On its day", "After its season"], list.FindAll("li span.font-medium").Select(s => s.TextContent));
        Assert.Equal(["Saved", "Couldn't save"], list.FindAll("li span.rounded-full").Select(s => s.TextContent));
        Assert.Equal("https://web.archive.org/web/20261203170000/https://blog.example.com/post", list.Find("a[href^='https://web.archive.org']").TextContent);
        Assert.Contains("Given up on after 5 tries. The Wayback Machine said: Not found. (error:not-found)", Text(list));
    }

    [Fact]
    public void ASnapshotStillToComeSaysWhen()
    {
        var text = Text(Render(
        [
            new(SnapshotMoment.Live, SnapshotStatus.Capturing, 0, Now, Now, null, null, null),
            new(SnapshotMoment.AfterSeason, SnapshotStatus.Due, 2, Now.AddHours(4), null, null, null, "The Wayback Machine said: Busy."),
        ]));

        Assert.Contains("The Wayback Machine has been saving it since Dec 3, 2026", text);
        Assert.Contains("The Wayback Machine said: Busy. Tried 2 times so far; asked for from Dec 3, 2026", text);
    }

    private IRenderedComponent<SnapshotList> Render(IReadOnlyList<SnapshotRow> rows) =>
        Render<SnapshotList>(p => p.Add(c => c.Rows, rows));

    private static string Text(IRenderedComponent<SnapshotList> list) =>
        System.Text.RegularExpressions.Regex.Replace(list.Find("div").TextContent, @"\s+", " ");
}
