namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Runs <see cref="SocialPostingService"/> every five minutes, so an announcement goes out within a few minutes of its time,
/// and <see cref="SocialCredentialCheck"/>, which checks each Automatic platform's credentials once a day. Runs with no
/// platform in Automatic mode, or nothing due, are cheap.
/// </summary>
public sealed class SocialPostingWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<SocialPostingWorker> logger)
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
                await scope.ServiceProvider.GetRequiredService<SocialCredentialCheck>().RunAsync(timeProvider.GetUtcNow(), stoppingToken);
                await scope.ServiceProvider.GetRequiredService<SocialPostingService>().RunAsync(timeProvider.GetUtcNow(), stoppingToken);
                await scope.ServiceProvider.GetRequiredService<SeasonSocialService>().RunAsync(timeProvider.GetUtcNow(), cancellationToken: stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again.
                logger.LogError(exception, "Automatic social posting failed. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
