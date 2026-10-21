namespace CSAdvent.Web.Reminders;

/// <summary>
/// Runs <see cref="ReminderService"/> and <see cref="Backups.BackupService"/> every 15 minutes. Runs with nothing to do
/// (before the send time, or with no spot needing a backup) are cheap.
/// </summary>
public sealed class ReminderWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ReminderWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(timeProvider.GetUtcNow(), stoppingToken);
                await scope.ServiceProvider.GetRequiredService<Backups.BackupService>().RunAsync(timeProvider.GetUtcNow(), cancellationToken: stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again.
                logger.LogError(exception, "Sending reminders failed. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
