using CSAdvent.Web.Announcements;
using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Retention;

/// <summary>
/// Runs <see cref="ContactRetention"/> when the app starts and every hour after that, so contact details are gone soon after
/// their deletion time without anyone having to remember, then deletes unconfirmed announcement subscribers. Each is a step
/// of its own (#147).
/// </summary>
public sealed class ContactRetentionWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<ContactRetentionWorker> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    public static readonly JobStep ContactDetails = new("contact-retention", "Deleting expired contact details", Every);

    public static readonly JobStep UnconfirmedSubscribers = new("unconfirmed-subscribers", "Deleting unconfirmed subscribers", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(ContactDetails, (services, _, cancellationToken) => services.GetRequiredService<ContactRetention>().DeleteExpiredAsync(cancellationToken)),
        new(UnconfirmedSubscribers, (services, _, cancellationToken) => services.GetRequiredService<AnnouncementSubscriptions>().DeleteUnconfirmedAsync(cancellationToken)),
    ];
}
