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
    /// How long before an entry's announcement (<see cref="Social.Posting.SocialPostingOptions.PostAt"/>) its author gets the
    /// day-of email, if their spot still has no post link: 7 AM for a 10 AM announcement.
    /// </summary>
    public TimeSpan DayOfLead { get; set; } = TimeSpan.FromHours(3);

    /// <summary>
    /// The latest the daily email waits for the rest of the day's posts, in the reveal time zone (#209). From
    /// <see cref="SendAt"/> it goes out as soon as every post of the day has its title and link; from this time, with
    /// whichever do. Empty means when the day's last post is announced
    /// (<see cref="Social.Posting.SocialPostingOptions.PostAt"/>: 2 PM).
    /// </summary>
    public TimeOnly? DailyEmailCutoff { get; set; }

    /// <summary>
    /// The pause after each email, to stay under Azure Communication Services' sending limit (30 a minute for a new
    /// custom domain).
    /// </summary>
    public TimeSpan PauseBetweenEmails { get; set; } = TimeSpan.FromSeconds(2);
}
