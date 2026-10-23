using CSAdvent.Web.Data;

namespace CSAdvent.Web.Calendar;

/// <summary>A season's calendar as the pages show it: one entry per slot, grouped by day.</summary>
public sealed record SeasonCalendar(
    int Year,
    int SlotsPerDay,
    string? Notes,
    DateOnly? ClaimsOpenOn,
    bool AreClaimsOpen,
    IReadOnlyList<CalendarDay> Days)
{
    public int PostCount => Days.Sum(day => day.Entries.Count(entry => entry.HasPost));

    public int SpotCount => Days.Sum(day => day.Entries.Count);

    /// <summary>Every day has been revealed: the season is over.</summary>
    public bool IsFinished => Days.Count > 0 && Days.All(day => day.Entries.All(entry => entry.IsRevealed));

    /// <summary>Spots nobody has claimed whose day hasn't been revealed yet, so they can still be claimed.</summary>
    public int OpenSpotCount => Days.Sum(day => day.Entries.Count(entry => entry.IsOpen && !entry.IsRevealed));

    public static SeasonCalendar From(Season season, AdventSchedule schedule)
    {
        var today = schedule.Today;

        return new(
            season.Year,
            season.SlotsPerDay,
            season.Notes,
            season.ClaimsOpenAt is { } opensAt ? DateOnly.FromDateTime(schedule.ToLocalTime(opensAt).DateTime) : null,
            // The test season is always open to its testers; nobody else gets its calendar (SeasonAccess).
            season.IsTest || schedule.AreClaimsOpen(season.ClaimsOpenAt),
            [.. season.Slots
                .GroupBy(slot => slot.Day)
                .OrderBy(day => day.Key)
                .Select(day => new CalendarDay(
                    day.Key,
                    schedule.GetRevealTime(season.Year, day.Key),
                    IsToday: today == schedule.GetDate(season.Year, day.Key),
                    [.. day.OrderBy(slot => slot.Part).Select(slot => CalendarEntry.From(slot, schedule.IsRevealed(slot)))]))]);
    }
}

public sealed record CalendarDay(int Day, DateTimeOffset RevealsAt, bool IsToday, IReadOnlyList<CalendarEntry> Entries);

public sealed record CalendarEntry(int Part, bool IsRevealed, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl)
{
    public bool HasPost => PostUrl is not null;

    /// <summary>Nobody has claimed the slot.</summary>
    public bool IsOpen => AuthorName is null;

    /// <summary>Before its reveal time, an entry never carries the post's title or link, so no page can leak them.</summary>
    public static CalendarEntry From(Slot slot, bool isRevealed) =>
        isRevealed
            ? new(slot.Part, IsRevealed: true, slot.AuthorName, slot.AuthorUrl, slot.PostTitle, slot.PostUrl)
            : new(slot.Part, IsRevealed: false, slot.AuthorName, slot.AuthorUrl, PostTitle: null, PostUrl: null);
}
