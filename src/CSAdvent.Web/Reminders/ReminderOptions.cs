using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Reminders;

public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    /// <summary>When each day's emails go out, in the reveal time zone. Runs before this time send nothing.</summary>
    public TimeOnly SendAt { get; set; } = new(9, 0);

    /// <summary>How many days before their day an author without a post link is reminded.</summary>
    [MinLength(1, ErrorMessage = "Reminders:DaysBefore needs at least one day.")]
    public int[] DaysBefore { get; set; } = [];

    /// <summary>
    /// The pause after each email, to stay under Azure Communication Services' sending limit (30 a minute for a new
    /// custom domain).
    /// </summary>
    public TimeSpan PauseBetweenEmails { get; set; } = TimeSpan.FromSeconds(2);
}
