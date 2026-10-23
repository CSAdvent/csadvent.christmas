using System.Globalization;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// The rehearsal as organizers think of it: what day today is in it ("today is December 6"), rather than the date December 1
/// falls on. A day is counted from December 1: 0 is December 1, -1 the day before, 25 is December 26.
/// </summary>
public static class RehearsalDays
{
    public const int Earliest = -14;
    public const int Latest = 25;

    /// <summary>Every day an organizer can make today: two weeks before Advent, every day of it, and the day after.</summary>
    public static IEnumerable<int> Choices => Enumerable.Range(Earliest, Latest - Earliest + 1);

    /// <summary>The date December 1 falls on, for today to be the given day.</summary>
    public static DateOnly December1For(DateOnly today, int day) => today.AddDays(-day);

    /// <summary>What day today is in a rehearsal whose December 1 is on <paramref name="december1"/>.</summary>
    public static int Today(DateOnly today, DateOnly december1) => today.DayNumber - december1.DayNumber;

    /// <summary>"3 days before December 1", "December 6", "December 26, after Advent".</summary>
    public static string Describe(int day) => day switch
    {
        -1 => "1 day before December 1 (Day 1 is revealed at midnight tonight)",
        < 0 => string.Create(CultureInfo.InvariantCulture, $"{-day} days before December 1"),
        >= 25 => string.Create(CultureInfo.InvariantCulture, $"December {day + 1}, after Advent"),
        _ => string.Create(CultureInfo.InvariantCulture, $"December {day + 1}"),
    };
}
