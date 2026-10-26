using CSAdvent.Web.Data;

namespace CSAdvent.Web.Reminders;

/// <summary>
/// Runs <see cref="Blurbs.BlurbCheckService"/>, <see cref="ReminderService"/> and <see cref="Backups.BackupService"/> every 15
/// minutes, in that order: the day's posts are checked for the blurb first, so the daily email leaves out a failed one, and a
/// blurb added just before its deadline is seen before a backup takes the spot. Runs with nothing to do are cheap.
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
                await scope.ServiceProvider.GetRequiredService<Blurbs.BlurbCheckService>().RunAsync(timeProvider.GetUtcNow(), cancellationToken: stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(timeProvider.GetUtcNow(), stoppingToken);
                await scope.ServiceProvider.GetRequiredService<Backups.BackupService>().RunAsync(timeProvider.GetUtcNow(), cancellationToken: stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
            {
                // Shortly after a deploy, before its migrations job has added a table or column this version uses (#151). The
                // next run after the migrations works, so it's a warning, not an error for the alert (#225).
                logger.LogWarning(exception, "Couldn't send reminders yet: the database isn't migrated to this version. Trying again in {Interval}.", Interval);
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
