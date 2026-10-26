using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Reminders;

/// <summary>
/// Runs <see cref="BlurbCheckService"/>, <see cref="ReminderService"/> and <see cref="BackupService"/> every 15 minutes, in
/// that order: the day's posts are checked for the blurb first, so the daily email leaves out a failed one, and a blurb added
/// just before its deadline is seen before a backup takes the spot. Each is a step of its own (#147), so a blurb check that
/// fails never stops the day's emails or backup asks. Runs with nothing to do are cheap.
/// </summary>
public sealed class ReminderWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<ReminderWorker> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(15);

    public static readonly JobStep BlurbCheck = new("blurb-check", "Blurb check", Every);

    public static readonly JobStep Reminders = new("reminders", "Reminders and the authors' daily email", Every);

    public static readonly JobStep Backups = new("backups", "Backup asks", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(BlurbCheck, (services, now, cancellationToken) => services.GetRequiredService<BlurbCheckService>().RunAsync(now, cancellationToken: cancellationToken)),
        new(Reminders, (services, now, cancellationToken) => services.GetRequiredService<ReminderService>().RunAsync(now, cancellationToken)),
        new(Backups, (services, now, cancellationToken) => services.GetRequiredService<BackupService>().RunAsync(now, cancellationToken: cancellationToken)),
    ];
}
