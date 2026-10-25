using System.Globalization;
using System.Text;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Calendar;

/// <summary>The add-to-calendar files (#207) for a season's sign-ups opening and its first reveal.</summary>
public static class CalendarFileLinks
{
    public static string SignUps(int year) => string.Create(CultureInfo.InvariantCulture, $"calendar/{year}/sign-ups.ics");

    public static string Starts(int year) => string.Create(CultureInfo.InvariantCulture, $"calendar/{year}/starts.ics");
}

/// <summary>One event in an iCalendar file, at a moment in time.</summary>
public sealed record CalendarEvent(string Uid, DateTimeOffset Start, TimeSpan Duration, string Summary, string Description, string Url);

/// <summary>The next season that hasn't started, for adding its dates to a calendar.</summary>
/// <param name="SignUpsOpenOn">The day its sign-ups open, in the reveal time zone, while that's still to come.</param>
public sealed record UpcomingDates(int Year, DateOnly? SignUpsOpenOn, DateOnly StartsOn);

/// <summary>
/// Writes iCalendar files (RFC 5545): CRLF line endings, lines folded at 75 octets, text escaped, and times in UTC, so every
/// calendar app shows the event in its reader's own time zone.
/// </summary>
public static class ICalendar
{
    public static string Write(CalendarEvent calendarEvent, DateTimeOffset stamp)
    {
        string[] lines =
        [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//C# Advent//csadvent.christmas//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{Text(calendarEvent.Uid)}",
            $"DTSTAMP:{Utc(stamp)}",
            $"DTSTART:{Utc(calendarEvent.Start)}",
            $"DTEND:{Utc(calendarEvent.Start + calendarEvent.Duration)}",
            $"SUMMARY:{Text(calendarEvent.Summary)}",
            $"DESCRIPTION:{Text(calendarEvent.Description)}",
            $"URL:{calendarEvent.Url}",
            "END:VEVENT",
            "END:VCALENDAR",
        ];
        return string.Concat(lines.Select(line => $"{Fold(line)}\r\n"));
    }

    /// <summary>A moment in UTC, such as <c>20261101T140000Z</c>.</summary>
    public static string Utc(DateTimeOffset moment) => moment.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A text value, with its backslashes, semicolons, commas and line breaks escaped.</summary>
    public static string Text(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// A line of more than 75 octets (UTF-8) folded onto continuation lines, which start with a space, without splitting a
    /// character.
    /// </summary>
    public static string Fold(string line)
    {
        var folded = new StringBuilder(line.Length);
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > 75)
            {
                folded.Append("\r\n ");
                octets = 1;
            }

            folded.Append(rune.ToString());
            octets += rune.Utf8SequenceLength;
        }

        return folded.ToString();
    }
}

/// <summary>
/// The add-to-calendar files (#207): "sign-ups open" (when a season's sign-ups are scheduled) and "C# Advent starts" (its first
/// reveal). Never for the test season, which nobody but its testers knows about.
/// </summary>
public sealed class CalendarFileService(AdventDbContext db, AdventSchedule schedule, IOptions<SiteOptions> site)
{
    /// <summary>How long each event lasts in a calendar.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    /// <summary>The season's "sign-ups open" file, or null if there's no such season, or its sign-ups aren't scheduled.</summary>
    public async Task<string?> SignUpsAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || await db.Seasons.AsNoTracking().Where(s => s.Year == year).Select(s => new { s.ClaimsOpenAt }).SingleOrDefaultAsync(cancellationToken) is not { ClaimsOpenAt: { } opensAt })
        {
            return null;
        }

        return ICalendar.Write(SignUpsEvent(year, opensAt, site.Value.BaseUrl), schedule.Now);
    }

    /// <summary>The season's "C# Advent starts" file, or null if there's no such season.</summary>
    public async Task<string?> StartsAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || !await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken))
        {
            return null;
        }

        return ICalendar.Write(StartsEvent(year, schedule.GetRevealTime(year, 1), site.Value.BaseUrl), schedule.Now);
    }

    /// <summary>The earliest season (never the test season) whose first reveal is still to come, or null if there isn't one yet.</summary>
    public async Task<UpcomingDates?> GetUpcomingAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await db.Seasons
            .AsNoTracking()
            .Where(s => s.Year != Season.TestYear)
            .OrderBy(s => s.Year)
            .Select(s => new { s.Year, s.ClaimsOpenAt })
            .ToListAsync(cancellationToken);
        var now = schedule.Now;
        if (seasons.FirstOrDefault(s => schedule.GetRevealTime(s.Year, 1) > now) is not { } next)
        {
            return null;
        }

        return new UpcomingDates(
            next.Year,
            next.ClaimsOpenAt is { } opensAt && opensAt > now ? DateOnly.FromDateTime(schedule.ToLocalTime(opensAt).DateTime) : null,
            schedule.GetDate(next.Year, 1));
    }

    public static CalendarEvent SignUpsEvent(int year, DateTimeOffset opensAt, string baseUrl) => new(
        string.Create(CultureInfo.InvariantCulture, $"csadvent-{year}-sign-ups@{Host(baseUrl)}"),
        opensAt,
        Duration,
        string.Create(CultureInfo.InvariantCulture, $"C# Advent {year}: sign-ups open"),
        string.Create(CultureInfo.InvariantCulture, $"Sign-ups for C# Advent {year} are open: claim a day in December and write a post about C# for the community. Two posts a day, December 1–25, and spots go quickly.\n\n{Home(baseUrl)}"),
        Home(baseUrl));

    public static CalendarEvent StartsEvent(int year, DateTimeOffset firstReveal, string baseUrl) => new(
        string.Create(CultureInfo.InvariantCulture, $"csadvent-{year}-starts@{Host(baseUrl)}"),
        firstReveal,
        Duration,
        string.Create(CultureInfo.InvariantCulture, $"C# Advent {year} starts"),
        string.Create(CultureInfo.InvariantCulture, $"The first posts of C# Advent {year} are revealed: two C# posts a day, December 1–25, written by the community.\n\n{Home(baseUrl)}"),
        Home(baseUrl));

    private static string Home(string baseUrl) => $"{baseUrl.TrimEnd('/')}/";

    private static string Host(string baseUrl) => new Uri(baseUrl).Host;
}
