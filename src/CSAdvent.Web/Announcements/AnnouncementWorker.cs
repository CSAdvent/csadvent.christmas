using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Announcements;

/// <summary>
/// Runs <see cref="AnnouncementService"/> every five minutes: queues the season's "sign-ups are open" email when it's due,
/// and puts readers' daily posts, announcements and past authors' invitations in the email queue, which sends them within
/// the email service's limits. Runs with nothing to queue are cheap.
/// </summary>
public sealed class AnnouncementWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<AnnouncementWorker> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    public static readonly JobStep Announcements = new("announcements", "Announcements, readers' daily posts and invitations", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(Announcements, (services, now, cancellationToken) => services.GetRequiredService<AnnouncementService>().RunAsync(now, cancellationToken: cancellationToken)),
    ];
}
