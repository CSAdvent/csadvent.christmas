using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.DeadLinks;

/// <summary>
/// The dead-link check (#201): how fast it goes through the archive's posts. It's slow on purpose. Each post is checked about
/// once a month, a few posts a run (every 15 minutes, <see cref="LinkCheckWorker.Every"/>), never the same site twice within
/// <see cref="HostPause"/>, so no author's site hears from us more than once an hour.
/// </summary>
public sealed class LinkCheckOptions
{
    public const string SectionName = "LinkChecks";

    /// <summary>The most posts checked in one run: at 2 a run, at most 192 a day, and a first pass over the archive in 2-3 days.</summary>
    [Range(1, 10)]
    public int PostsPerRun { get; set; } = 2;

    /// <summary>How long after a check a site's next post can be checked, so a site with many posts (dev.to, say) is checked slowly.</summary>
    public TimeSpan HostPause { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long a post that loaded (or is dead, or turned the check away) waits for its next check, give or take 10%.</summary>
    public TimeSpan RecheckAfter { get; set; } = TimeSpan.FromDays(30);

    /// <summary>A post that didn't load is checked again after this long, times the failures so far: a day, then two.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Failed checks in a row that make a post dead. At 3, it's been failing for 3 days, so it isn't a passing outage.</summary>
    [Range(1, 10)]
    public int FailuresBeforeDead { get; set; } = 3;

    /// <summary>The most dead posts whose archived copy is looked up in one run.</summary>
    [Range(0, 10)]
    public int ArchiveLookupsPerRun { get; set; } = 2;

    /// <summary>How long a dead post the Wayback Machine has no copy of waits before it's asked again.</summary>
    public TimeSpan ArchiveRecheckAfter { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// When the organizers get the day's email about posts found dead (<see cref="DeadLinkEmail"/>), in the reveal time zone:
    /// every post found dead before it that they haven't heard about.
    /// </summary>
    public TimeOnly EmailAt { get; set; } = new(8, 0);

    /// <summary>
    /// Development and the tests: posts are only "fetched" from the stand-in (<see cref="SampleLinkProber"/>), and archived
    /// copies only "found" by its counterpart (<see cref="SampleArchiveLookup"/>), so nothing reaches authors' sites or the
    /// Internet Archive.
    /// </summary>
    public bool SamplePages { get; set; }
}
