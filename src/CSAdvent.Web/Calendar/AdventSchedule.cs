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

    /// <summary>A season is over, and in the archive, once its last day is revealed.</summary>
    public bool IsSeasonOver(int year) => IsRevealed(year, Season.DaysInAdvent);

    /// <summary>
    /// When a season's contact details are deleted: the start of February 1 after it, in the reveal time zone. The privacy
    /// page promises this date, and <see cref="Retention.ContactRetention"/> keeps the promise.
    /// </summary>
    public DateTimeOffset GetContactDeletionTime(int year) => FromLocalTime(new DateTime(year + 1, 2, 1));

    /// <summary>Claims are open from the season's <see cref="Season.ClaimsOpenAt"/>; never if it isn't scheduled.</summary>
    public bool AreClaimsOpen(DateTimeOffset? claimsOpenAt) => claimsOpenAt is { } opensAt && _timeProvider.GetUtcNow() >= opensAt;

    /// <summary>Today's date in the reveal time zone.</summary>
    public DateOnly Today => DateOnly.FromDateTime(ToLocalTime(_timeProvider.GetUtcNow()).DateTime);

    public DateTimeOffset ToLocalTime(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, TimeZone);

    /// <summary>A date and time in the reveal time zone (such as a <c>datetime-local</c> form value), as UTC.</summary>
    public DateTimeOffset FromLocalTime(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
