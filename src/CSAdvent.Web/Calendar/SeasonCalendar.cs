using CSAdvent.Web.Data;

namespace CSAdvent.Web.Calendar;

/// <summary>A season's calendar as the pages show it: one entry per slot, grouped by day.</summary>
public sealed record SeasonCalendar(int Year, int SlotsPerDay, string? Notes, IReadOnlyList<CalendarDay> Days)
{
    public int PostCount => Days.Sum(day => day.Entries.Count(entry => entry.HasPost));

    public static SeasonCalendar From(Season season, AdventSchedule schedule) =>
        new(
            season.Year,
            season.SlotsPerDay,
            season.Notes,
            [.. season.Slots
                .GroupBy(slot => slot.Day)
                .OrderBy(day => day.Key)
                .Select(day => new CalendarDay(
                    day.Key,
                    schedule.GetRevealTime(season.Year, day.Key),
                    [.. day.OrderBy(slot => slot.Part).Select(slot => CalendarEntry.From(slot, schedule.IsRevealed(slot)))]))]);
}

public sealed record CalendarDay(int Day, DateTimeOffset RevealsAt, IReadOnlyList<CalendarEntry> Entries);

public sealed record CalendarEntry(int Part, bool IsRevealed, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl)
{
    public bool HasPost => PostUrl is not null;

    /// <summary>Before its reveal time, an entry never carries the post's title or link, so no page can leak them.</summary>
    public static CalendarEntry From(Slot slot, bool isRevealed) =>
        isRevealed
            ? new(slot.Part, IsRevealed: true, slot.AuthorName, slot.AuthorUrl, slot.PostTitle, slot.PostUrl)
            : new(slot.Part, IsRevealed: false, slot.AuthorName, slot.AuthorUrl, PostTitle: null, PostUrl: null);
}
