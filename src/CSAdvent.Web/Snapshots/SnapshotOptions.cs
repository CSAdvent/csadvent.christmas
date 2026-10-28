using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Snapshots;

/// <summary>
/// The Wayback Machine snapshots (#261): asked for through the Internet Archive's Save Page Now API, a few a run (every 15
/// minutes, <see cref="SnapshotWorker.Every"/>), well under its limits for an account (7 a minute, 30,000 a day).
/// </summary>
public sealed class SnapshotOptions
{
    public const string SectionName = "Snapshots";

    /// <summary>
    /// An archive.org account's S3 keys, as <c>accesskey:secret</c> (a secret, never in source). Without them, nothing is saved.
    /// </summary>
    public string? Keys { get; set; }

    /// <summary>The most snapshots asked for in one run: at 4, 16 an hour, so a first pass over the archive takes about a day.</summary>
    [Range(1, 10)]
    public int CapturesPerRun { get; set; } = 4;

    /// <summary>
    /// How long after its day is over a post can still get its snapshot as it went live. After that it waits for the one
    /// after its season.
    /// </summary>
    public TimeSpan LiveWithin { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// How long a snapshot the Wayback Machine couldn't save waits to be asked for again, doubling each time (an hour, then
    /// two, four and eight). One it didn't answer about waits this long, and doesn't count as a try.
    /// </summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Tries before a snapshot is given up on: at 5, about 15 hours after the first.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long a capture can go without saying it's done. Save Page Now only keeps a job's result for about an hour, so after
    /// that it's taken as not saved, and tried again.
    /// </summary>
    public TimeSpan JobExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Development and the tests: snapshots are only "saved" by a stand-in (<see cref="SampleSnapshotSaver"/>), so nothing
    /// reaches the Internet Archive.
    /// </summary>
    public bool SamplePages { get; set; }

    public bool HasKeys => !string.IsNullOrWhiteSpace(Keys);

    /// <summary>Whether snapshots are saved at all: with keys, or with the stand-in.</summary>
    public bool IsEnabled => HasKeys || SamplePages;
}
