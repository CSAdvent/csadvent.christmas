using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;

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

    /// <summary>Every day has been revealed: the calendar is complete (<see cref="AdventSchedule.IsLastDayRevealed"/>).</summary>
    public bool IsComplete => Days.Count > 0 && Days.All(day => day.Entries.All(entry => entry.IsRevealed));

    /// <summary>Spots nobody has claimed whose day hasn't been revealed yet, so they can still be claimed.</summary>
    public int OpenSpotCount => Days.Sum(day => day.Entries.Count(entry => entry.IsOpen && !entry.IsRevealed));

    /// <summary>When the calendar was put together: what "now" means for its reveals.</summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>The signed-in contributor's own spots and requests, on the home page (#175). None anywhere else.</summary>
    public CalendarMarks Marks { get; init; } = CalendarMarks.None;

    /// <summary>How close the next reveal has to be for its day to count down to it, as "tomorrow" on the old site did.</summary>
    public static readonly TimeSpan CountdownWithin = TimeSpan.FromHours(24);

    /// <summary>The day revealed next, if it's within <see cref="CountdownWithin"/>, for its countdown (#31).</summary>
    public CalendarDay? CountingDownTo =>
        Days.FirstOrDefault(day => day.RevealsAt > GeneratedAt) is { } next && next.RevealsAt - GeneratedAt <= CountdownWithin ? next : null;

    public static SeasonCalendar From(Season season, AdventSchedule schedule)
    {
        // One reading of the clock, so the reveals, today and the countdown agree even across midnight (#184).
        var now = schedule.Now;
        var today = schedule.DateOf(now);

        return new(
            season.Year,
            season.SlotsPerDay,
            season.Notes,
            season.ClaimsOpenAt is { } opensAt ? DateOnly.FromDateTime(schedule.ToLocalTime(opensAt).DateTime) : null,
            // The test season is always open to its testers; nobody else gets its calendar (SeasonAccess).
            season.IsTest || schedule.AreClaimsOpen(season.ClaimsOpenAt, now),
            [.. season.Slots
                .GroupBy(slot => slot.Day)
                .OrderBy(day => day.Key)
                .Select(day => new CalendarDay(
                    day.Key,
                    schedule.GetRevealTime(season.Year, day.Key),
                    IsToday: today == schedule.GetDate(season.Year, day.Key),
                    [.. day.OrderBy(slot => slot.Part).Select(slot => CalendarEntry.From(slot, schedule.IsRevealed(slot, now)))]))])
        {
            GeneratedAt = now,
        };
    }
}

public sealed record CalendarDay(int Day, DateTimeOffset RevealsAt, bool IsToday, IReadOnlyList<CalendarEntry> Entries);

/// <summary>Which spots on a calendar are the signed-in contributor's, and which they've asked for (#175).</summary>
public sealed record CalendarMarks(IReadOnlySet<(int Day, int Part)> Mine, IReadOnlySet<(int Day, int Part)> Requested)
{
    public static readonly CalendarMarks None = new(new HashSet<(int, int)>(), new HashSet<(int, int)>());

    public bool IsMine(int day, int part) => Mine.Contains((day, part));

    public bool IsRequested(int day, int part) => Requested.Contains((day, part));
}

public sealed record CalendarEntry(int Part, bool IsRevealed, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl)
{
    /// <summary>The post's title and URL are both in: the calendar links the title to the post, so it needs both, like its page.</summary>
    public bool HasPost => PostUrl is not null && !string.IsNullOrWhiteSpace(PostTitle);

    /// <summary>Nobody has claimed the slot.</summary>
    public bool IsOpen => AuthorName is null;

    /// <summary>
    /// Before its reveal time, an entry never carries the post's title or link, so no page can leak them. The author's name
    /// links to their website, or to their GitHub profile (with the slot's contributor loaded).
    /// </summary>
    public static CalendarEntry From(Slot slot, bool isRevealed)
    {
        var authorUrl = AuthorLinks.ForName(slot.AuthorUrl, slot.Contributor?.GithubLogin);
        return isRevealed
            ? new(slot.Part, IsRevealed: true, slot.AuthorName, authorUrl, slot.PostTitle, slot.PostUrl)
            : new(slot.Part, IsRevealed: false, slot.AuthorName, authorUrl, PostTitle: null, PostUrl: null);
    }
}
