namespace CSAdvent.Web.Content;

/// <summary>
/// Fetches logos for Other Advents calendars that don't have one, every minute. Most runs find nothing to do; a calendar an
/// organizer just added gets its logo within a minute or so.
/// </summary>
public sealed class OtherAdventLogoWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<OtherAdventLogoWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var logos = scope.ServiceProvider.GetRequiredService<OtherAdventLogoService>();
                foreach (var url in await logos.FindMissingAsync(stoppingToken))
                {
                    // Null when another replica took it first.
                    if (await logos.FetchIfMissingAsync(url, stoppingToken) is { } result)
                    {
                        logger.LogInformation("Logo for {Url}: {Result}.", url, result);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // An unhandled exception would stop the whole app. The next run tries again (and on a brand-new deployment
                // the first run can come before the migrations job has created the tables).
                logger.LogError(exception, "Fetching Other Advents logos failed. Trying again in {Interval}.", Interval);
            }
        }
    }
}
