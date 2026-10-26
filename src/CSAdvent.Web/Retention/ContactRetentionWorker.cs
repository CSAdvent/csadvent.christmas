using CSAdvent.Web.Data;

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
                await scope.ServiceProvider.GetRequiredService<Announcements.AnnouncementSubscriptions>().DeleteUnconfirmedAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
            {
                // Shortly after a deploy, before its migrations job has added a table or column this version uses (#151). The
                // next run after the migrations works, so it's a warning, not an error for the alert (#225).
                logger.LogWarning(exception, "Couldn't delete expired contact details yet: the database isn't migrated to this version. Trying again in {Interval}.", Interval);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again.
                logger.LogError(exception, "Deleting expired contact details failed. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
