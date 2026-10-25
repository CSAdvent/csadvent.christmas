using System.Text;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Calendar;

public sealed class CalendarFileTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 25, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void WritesAValidEventInUtcWithCrlfLineEndings()
    {
        var opensAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(-4));

        var file = ICalendar.Write(CalendarFileService.SignUpsEvent(2026, opensAt, "https://csadvent.christmas"), Stamp);

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//C# Advent//csadvent.christmas//EN\r\n", file);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", file);
        Assert.DoesNotContain("\n", file.Replace("\r\n", "", StringComparison.Ordinal));
        var lines = Unfold(file);
        Assert.Contains("UID:csadvent-2026-sign-ups@csadvent.christmas", lines);
        Assert.Contains("DTSTAMP:20261025T163000Z", lines);
        Assert.Contains("DTSTART:20261101T140000Z", lines);
        Assert.Contains("DTEND:20261101T150000Z", lines);
        Assert.Contains("SUMMARY:C# Advent 2026: sign-ups open", lines);
        Assert.Contains("URL:https://csadvent.christmas/", lines);
        Assert.Contains(
            "DESCRIPTION:Sign-ups for C# Advent 2026 are open: claim a day in December and write a post about C# for the community. "
                + "Two posts a day\\, December 1–25\\, and spots go quickly.\\n\\nhttps://csadvent.christmas/",
            lines);
    }

    [Fact]
    public void TheStartIsTheFirstReveal()
    {
        var schedule = CalendarFixtures.Schedule(Stamp);

        var lines = Unfold(ICalendar.Write(CalendarFileService.StartsEvent(2026, schedule.GetRevealTime(2026, 1), "https://csadvent.christmas"), Stamp));

        // Midnight Eastern on December 1.
        Assert.Contains("DTSTART:20261201T050000Z", lines);
        Assert.Contains("UID:csadvent-2026-starts@csadvent.christmas", lines);
        Assert.Contains("SUMMARY:C# Advent 2026 starts", lines);
    }

    [Fact]
    public void EscapesText() =>
        Assert.Equal("a\\,b\\;c\\\\d\\ne\\nf", ICalendar.Text("a,b;c\\d\ne\r\nf"));

    [Fact]
    public void FoldsLongLinesAt75OctetsWithoutSplittingACharacter()
    {
        var line = $"DESCRIPTION:{new string('é', 60)}{new string('x', 40)}";

        var folded = ICalendar.Fold(line);

        var parts = folded.Split("\r\n");
        Assert.True(parts.Length > 1);
        Assert.All(parts, part => Assert.True(Encoding.UTF8.GetByteCount(part) <= 75, $"{part} is too long."));
        Assert.All(parts.Skip(1), part => Assert.StartsWith(" ", part));
        Assert.Equal(line, string.Concat(parts.Select((part, i) => i == 0 ? part : part[1..])));
        Assert.Equal("SUMMARY:short", ICalendar.Fold("SUMMARY:short"));
    }

    [Fact]
    public async Task NeverForTheTestSeason()
    {
        var files = new CalendarFileService(
            null!,
            CalendarFixtures.Schedule(Stamp),
            Options.Create(new SiteOptions { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" }));

        Assert.Null(await files.SignUpsAsync(Season.TestYear, TestContext.Current.CancellationToken));
        Assert.Null(await files.StartsAsync(Season.TestYear, TestContext.Current.CancellationToken));
    }

    /// <summary>The file's lines with folded ones joined back up.</summary>
    private static List<string> Unfold(string file) => [.. file.Replace("\r\n ", "", StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];
}
