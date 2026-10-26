using CSAdvent.Web.Data;

namespace CSAdvent.Web.Announcements;

/// <summary>
/// Runs <see cref="AnnouncementService"/> every five minutes: queues the season's "sign-ups are open" email when it's due,
/// and puts readers' daily posts, announcements and past authors' invitations in the email queue, which sends them within
/// the email service's limits. Runs with nothing to queue are cheap.
/// </summary>
public sealed class AnnouncementWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<AnnouncementWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AnnouncementService>().RunAsync(timeProvider.GetUtcNow(), cancellationToken: stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested && MissingSchema.IsMissingTableOrColumn(exception))
            {
                // Shortly after a deploy, before its migrations job has added a table or column this version uses (#151). The
                // next run after the migrations works, so it's a warning, not an error for the alert (#225).
                logger.LogWarning(exception, "Couldn't send announcements yet: the database isn't migrated to this version. Trying again in {Interval}.", Interval);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again.
                logger.LogError(exception, "Sending announcements failed. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
