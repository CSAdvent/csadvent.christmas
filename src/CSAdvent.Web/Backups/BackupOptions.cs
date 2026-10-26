using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Backups;

/// <summary>When backup posters are asked, and for how long. Checked when the app starts (#188).</summary>
public sealed class BackupOptions : IValidatableObject
{
    public const string SectionName = "Backups";

    /// <summary>
    /// When, on the evening before a spot's day (in the reveal time zone), we start asking backups if the spot still has no
    /// post link. Spots whose author dropped out are offered straight away.
    /// </summary>
    public TimeOnly AskFrom { get; set; } = new(18, 0);

    /// <summary>How long each backup has to answer before the next one is asked too.</summary>
    public TimeSpan AnswerWithin { get; set; } = TimeSpan.FromHours(4);

    /// <summary>The same for a post whose C# Advent blurb is missing on its day (#128), when the deadline is only hours away.</summary>
    public TimeSpan AnswerWithinOnTheDay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// For a post the blurb check found without the blurb: how long the organizers have, after they hear that a backup is
    /// standing by, to keep the author before the backup takes the spot (#148). The backup never takes it before the deadline.
    /// </summary>
    public TimeSpan OrganizersNotice { get; set; } = TimeSpan.FromHours(1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AnswerWithin <= TimeSpan.Zero || AnswerWithinOnTheDay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{SectionName}:AnswerWithin and {SectionName}:AnswerWithinOnTheDay must be longer than zero: each backup needs time to answer.",
                [nameof(AnswerWithin), nameof(AnswerWithinOnTheDay)]);
        }

        if (OrganizersNotice < TimeSpan.Zero)
        {
            yield return new ValidationResult($"{SectionName}:OrganizersNotice can't be negative.", [nameof(OrganizersNotice)]);
        }
    }
}
