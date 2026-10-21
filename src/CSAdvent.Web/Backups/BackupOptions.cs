namespace CSAdvent.Web.Backups;

public sealed class BackupOptions
{
    public const string SectionName = "Backups";

    /// <summary>
    /// When, on the evening before a spot's day (in the reveal time zone), we start asking backups if the spot still has no
    /// post link. Spots whose author dropped out are offered straight away.
    /// </summary>
    public TimeOnly AskFrom { get; set; } = new(18, 0);

    /// <summary>How long each backup has to answer before the next one is asked too.</summary>
    public TimeSpan AnswerWithin { get; set; } = TimeSpan.FromHours(4);
}
