namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// Reloads <see cref="TestSeasons"/> when the app starts and every 15 seconds, so a change made on one replica reaches the
/// others. It's one small query, and the replica that made the change reloads straight away.
/// </summary>
public sealed class TestSeasonsRefresher(TestSeasons testSeasons, TimeProvider timeProvider, ILogger<TestSeasonsRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await testSeasons.RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // On a brand-new deployment the first runs can come before the migrations job has created the tables.
                logger.LogWarning(exception, "Couldn't load the seasons being tested. Trying again in {Interval}.", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
