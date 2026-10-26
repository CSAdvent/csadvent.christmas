namespace CSAdvent.Web.Calendar;

public sealed class AdventOptions
{
    public const string SectionName = "Advent";

    /// <summary>
    /// The time zone (an IANA ID such as <c>America/New_York</c>) in which each day's posts are revealed at midnight.
    /// The server and the browser countdown both use it.
    /// </summary>
    public string TimeZone { get; set; } = "";

    /// <summary>
    /// What emails and pages call <see cref="TimeZone"/> when they give a time, as in "10:00 AM Eastern on December 6"
    /// (<see cref="AdventSchedule.Describe"/>). Change it with the time zone.
    /// </summary>
    public string TimeZoneName { get; set; } = "Eastern";
}
