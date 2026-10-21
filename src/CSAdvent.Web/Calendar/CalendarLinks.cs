namespace CSAdvent.Web.Calendar;

public static class CalendarLinks
{
    /// <summary>The page where a contributor requests a slot.</summary>
    public static string Claim(int year, int day, int part) => $"claim/{year}/{day}/{part}";
}
