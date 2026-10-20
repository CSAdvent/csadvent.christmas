namespace CSAdvent.Web.Calendar;

public sealed class AdventOptions
{
    public const string SectionName = "Advent";

    /// <summary>
    /// The time zone (an IANA ID such as <c>America/New_York</c>) in which each day's posts are revealed at midnight.
    /// The server and the browser countdown both use it.
    /// </summary>
    public string TimeZone { get; set; } = "";
}
