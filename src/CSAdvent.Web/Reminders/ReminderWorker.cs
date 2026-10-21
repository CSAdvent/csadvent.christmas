namespace CSAdvent.Web.Reminders;

/// <summary>
/// Runs <see cref="ReminderService"/> every 15 minutes. Runs before the send time, or after everything for the day has
/// gone out, do nothing.
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
