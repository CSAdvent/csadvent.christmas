using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Calendar;

/// <summary>
/// When each day's posts are revealed: midnight on December {day} of the season's year, in the configured time zone. The
/// test season's days follow its rehearsal instead (<see cref="TestSeasons"/>), so everything built on these dates (reveals,
/// reminders, backup asks) can be tried any time. Reveal times are always computed, never stored.
/// </summary>
public sealed class AdventSchedule
{
    private readonly TimeProvider _timeProvider;
    private readonly TestSeasons? _testSeasons;

    public AdventSchedule(IOptions<AdventOptions> options, TimeProvider timeProvider, TestSeasons? testSeasons = null)
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        _timeProvider = timeProvider;
        _testSeasons = testSeasons;
    }

    public TimeZoneInfo TimeZone { get; }

    /// <summary>The date of a day of Advent: December {day}, or the matching day of the test season's rehearsal.</summary>
    public DateOnly GetDate(int year, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, Season.DaysInAdvent);

        return _testSeasons?.Find(year) is { } rehearsal ? rehearsal.RehearsalStartsOn.AddDays(day - 1) : new DateOnly(year, 12, day);
    }

    /// <summary>Which day of Advent a date is in the season (1 to 25), or null if it's outside Advent.</summary>
    public int? GetDayOfAdvent(int year, DateOnly date) =>
        (date.DayNumber - GetDate(year, 1).DayNumber + 1) is var day and >= 1 and <= Season.DaysInAdvent ? day : null;

    /// <summary>
    /// A date as the season sees it: the date itself for a real season, and for the test season the date its rehearsal makes
    /// it, with the rehearsal's December 1 on December 1 of the season's year. Emails sent once a day are recorded against
    /// this date, so moving the rehearsal sends the new day's emails, even on the same real day.
    /// </summary>
    public DateOnly InSeason(int year, DateOnly date) => new DateOnly(year, 12, 1).AddDays(date.DayNumber - GetDate(year, 1).DayNumber);

    /// <summary>Whether the season's days follow a rehearsal rather than December: only the test season's do.</summary>
    public bool IsRehearsing(int year) => _testSeasons?.Find(year) is not null;

    public DateTimeOffset GetRevealTime(int year, int day)
    {
        var midnight = GetDate(year, day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, TimeZone.GetUtcOffset(midnight));
    }

    public DateTimeOffset GetRevealTime(Slot slot) => GetRevealTime(slot.SeasonYear, slot.Day);

    public bool IsRevealed(int year, int day) => _timeProvider.GetUtcNow() >= GetRevealTime(year, day);

    public bool IsRevealed(Slot slot) => IsRevealed(slot.SeasonYear, slot.Day);

    /// <summary>When a day ends: midnight at the start of the next day, in the reveal time zone.</summary>
    public DateTimeOffset GetDayEndTime(int year, int day) => FromLocalTime(GetDate(year, day).AddDays(1).ToDateTime(TimeOnly.MinValue));

    public bool IsDayOver(int year, int day) => _timeProvider.GetUtcNow() >= GetDayEndTime(year, day);

    /// <summary>A season is over, and in the archive, once its last day is revealed.</summary>
    public bool IsSeasonOver(int year) => IsRevealed(year, Season.DaysInAdvent);

    /// <summary>
    /// When the home page starts showing a season's calendar: when its sign-ups open, or at the first reveal if that's
    /// sooner or sign-ups aren't scheduled.
    /// </summary>
    public DateTimeOffset GetCalendarStart(int year, DateTimeOffset? claimsOpenAt)
    {
        var firstReveal = GetRevealTime(year, 1);
        return claimsOpenAt is { } opensAt && opensAt < firstReveal ? opensAt : firstReveal;
    }

    /// <summary>When the home page stops showing a season's calendar: the end of December, so people can catch up over the holidays.</summary>
    public DateTimeOffset GetCalendarEnd(int year) => FromLocalTime(new DateTime(year + 1, 1, 1));

    /// <summary>Whether the home page shows this season's calendar now, rather than the page between seasons.</summary>
    public bool IsCalendarShown(int year, DateTimeOffset? claimsOpenAt)
    {
        var now = _timeProvider.GetUtcNow();
        return now >= GetCalendarStart(year, claimsOpenAt) && now < GetCalendarEnd(year);
    }

    /// <summary>
    /// When a season's contact details are deleted: the start of February 1 after it, in the reveal time zone. The privacy
    /// page promises this date, and <see cref="Retention.ContactRetention"/> keeps the promise.
    /// </summary>
    public DateTimeOffset GetContactDeletionTime(int year) => FromLocalTime(new DateTime(year + 1, 2, 1));

    /// <summary>Claims are open from the season's <see cref="Season.ClaimsOpenAt"/>; never if it isn't scheduled.</summary>
    public bool AreClaimsOpen(DateTimeOffset? claimsOpenAt) => claimsOpenAt is { } opensAt && _timeProvider.GetUtcNow() >= opensAt;

    /// <summary>Today's date in the reveal time zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(ToLocalTime(_timeProvider.GetUtcNow()).DateTime);

    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public DateTimeOffset ToLocalTime(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, TimeZone);

    /// <summary>A date and time in the reveal time zone (such as a <c>datetime-local</c> form value), as UTC.</summary>
    public DateTimeOffset FromLocalTime(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
