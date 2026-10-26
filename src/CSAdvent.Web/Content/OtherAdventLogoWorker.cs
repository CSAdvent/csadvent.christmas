using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Content;

/// <summary>
/// Fetches logos for Other Advents calendars that don't have one, every minute. Most runs find nothing to do; a calendar an
/// organizer just added gets its logo within a minute or so.
/// </summary>
public sealed partial class OtherAdventLogoWorker : PeriodicWorker
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    public static readonly JobStep Logos = new("other-advent-logos", "Other Advents logos", Every);

    private readonly ILogger<OtherAdventLogoWorker> _logger;

    public OtherAdventLogoWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<OtherAdventLogoWorker> logger)
        : base(scopeFactory, runs, timeProvider, logger, Every)
    {
        _logger = logger;
        Steps = [new(Logos, FetchMissingAsync)];
    }

    protected override IReadOnlyList<PeriodicStep> Steps { get; }

    protected override bool RunsAtStart => false;

    private async Task FetchMissingAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var logos = services.GetRequiredService<OtherAdventLogoService>();
        foreach (var url in await logos.FindMissingAsync(cancellationToken))
        {
            // Null when another replica took it first.
            if (await logos.FetchIfMissingAsync(url, cancellationToken) is { } result)
            {
                LogFetched(url, result);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logo for {Url}: {Result}.")]
    private partial void LogFetched(string url, LogoResult result);
}
