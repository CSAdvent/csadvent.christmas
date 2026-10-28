using CSAdvent.Web.Announcements;
using CSAdvent.Web.Content;
using CSAdvent.Web.DeadLinks;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Retention;
using CSAdvent.Web.SeasonTesting;
using CSAdvent.Web.Snapshots;
using CSAdvent.Web.Social.Posting;

namespace CSAdvent.Web.Jobs;

/// <summary>Every background job step, in the order /manage lists them (#172). Add a new worker's steps here.</summary>
public static class JobSteps
{
    public static readonly IReadOnlyList<JobStep> All =
    [
        ReminderWorker.BlurbCheck,
        ReminderWorker.Reminders,
        ReminderWorker.Backups,
        ReminderWorker.Digest,
        SocialPostingWorker.CredentialCheck,
        SocialPostingWorker.Announcements,
        SocialPostingWorker.SeasonPosts,
        AnnouncementWorker.Announcements,
        EmailOutboxWorker.Step,
        ContactRetentionWorker.ContactDetails,
        ContactRetentionWorker.UnconfirmedSubscribers,
        OtherAdventLogoWorker.Logos,
        LinkCheckWorker.DeadLinks,
        LinkCheckWorker.DeadLinksEmail,
        SnapshotWorker.Snapshots,
        TestSeasonsRefresher.Refresh,
    ];
}
