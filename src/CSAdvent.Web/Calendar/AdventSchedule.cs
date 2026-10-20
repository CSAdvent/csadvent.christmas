using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Calendar;

/// <summary>
/// When each day's posts are revealed: midnight on December {day} of the season's year, in the configured time zone.
/// Reveal times are always computed from the season, the day and the time zone, never stored.
/// </summary>
public sealed class AdventSchedule
{
    private readonly TimeProvider _timeProvider;

    public AdventSchedule(IOptions<AdventOptions> options, TimeProvider timeProvider)
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        _timeProvider = timeProvider;
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset GetRevealTime(int year, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, Season.DaysInAdvent);

        var midnight = new DateTime(year, 12, day, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, TimeZone.GetUtcOffset(midnight));
    }

    public DateTimeOffset GetRevealTime(Slot slot) => GetRevealTime(slot.SeasonYear, slot.Day);

    public bool IsRevealed(int year, int day) => _timeProvider.GetUtcNow() >= GetRevealTime(year, day);

    public bool IsRevealed(Slot slot) => IsRevealed(slot.SeasonYear, slot.Day);
}
