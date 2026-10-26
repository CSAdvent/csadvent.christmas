using CSAdvent.Web.Jobs;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Runs <see cref="SocialPostingService"/> every five minutes, so an announcement goes out within a few minutes of its time,
/// after <see cref="SocialCredentialCheck"/>, which checks each Automatic platform's credentials once a day, and before
/// <see cref="SeasonSocialService"/>'s "sign-ups are open" posts. Each is a step of its own (#147), so a credential check
/// that fails never stops the day's announcements. Runs with no platform in Automatic mode, or nothing due, are cheap.
/// </summary>
public sealed class SocialPostingWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger<SocialPostingWorker> logger)
    : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Every)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    public static readonly JobStep CredentialCheck = new("social-credentials", "Social credential check", Every);

    public static readonly JobStep Announcements = new("social-posting", "Automatic social posts", Every);

    public static readonly JobStep SeasonPosts = new("season-social-posts", "\"Sign-ups are open\" social posts", Every);

    protected override IReadOnlyList<PeriodicStep> Steps { get; } =
    [
        new(CredentialCheck, (services, now, cancellationToken) => services.GetRequiredService<SocialCredentialCheck>().RunAsync(now, cancellationToken)),
        new(Announcements, (services, now, cancellationToken) => services.GetRequiredService<SocialPostingService>().RunAsync(now, cancellationToken)),
        new(SeasonPosts, (services, now, cancellationToken) => services.GetRequiredService<SeasonSocialService>().RunAsync(now, cancellationToken: cancellationToken)),
    ];
}
