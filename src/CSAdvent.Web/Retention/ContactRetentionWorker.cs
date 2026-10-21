namespace CSAdvent.Web.Retention;

/// <summary>
/// Runs <see cref="ContactRetention"/> when the app starts and every hour after that, so contact details are gone soon after
/// their deletion time without anyone having to remember.
/// </summary>
public sealed class ContactRetentionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ContactRetentionWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ContactRetention>().DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again (and on a brand-new deployment
                // the first run can come before the migrations job has created the tables).
                logger.LogError(exception, "Deleting expired contact details failed. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
