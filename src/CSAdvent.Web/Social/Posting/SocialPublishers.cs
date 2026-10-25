using CSAdvent.Web.Claims;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// The publisher for each platform that can post automatically: the real one when its credentials are configured, a
/// logging stand-in in dry-run mode (development and the tests), and otherwise none, so the platform stays copy and paste.
/// LinkedIn and Reddit are always copy and paste (#60).
/// </summary>
public sealed class SocialPublishers(
    IOptions<SocialPostingOptions> posting,
    IOptions<SocialAccountsOptions> accounts,
    [FromKeyedServices(SocialPublishers.ClientKey)] HttpClient http,
    TimeProvider timeProvider,
    ILogger<DryRunPublisher> dryRunLogger)
{
    public const string ClientKey = "social-posting";

    public static readonly IReadOnlyList<SocialPlatform> Automatable = [SocialPlatform.Mastodon, SocialPlatform.Bluesky, SocialPlatform.Facebook, SocialPlatform.X];

    public ISocialPublisher? For(SocialPlatform platform)
    {
        var options = posting.Value;
        var ours = accounts.Value;
        return platform switch
        {
            SocialPlatform.Mastodon when options.Mastodon.IsConfigured && ours.MastodonServer is { } server =>
                new MastodonPublisher(http, server, options.Mastodon.AccessToken!),
            SocialPlatform.Bluesky when options.Bluesky.IsConfigured && ours.Bluesky is { Length: > 0 } handle =>
                new BlueskyPublisher(http, handle, options.Bluesky, timeProvider),
            SocialPlatform.Facebook when options.Facebook.IsConfigured => new FacebookPublisher(http, options.Facebook),
            SocialPlatform.X when options.X.IsConfigured => new XPublisher(http, ours.X, options.X, timeProvider),
            _ when options.DryRun && Automatable.Contains(platform) => new DryRunPublisher(platform, dryRunLogger),
            _ => null,
        };
    }

    /// <summary>
    /// A client of its own rather than one from the factory, because the factory's default retries could post an
    /// announcement twice when a request times out after the platform accepted it. Failed posts are retried by
    /// <see cref="SocialPostingService"/>, which knows whether they went out.
    /// </summary>
    public static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas)");
        return client;
    }
}

/// <summary>Logs the announcement instead of posting it: automatic posting without credentials, in development and the tests.</summary>
public sealed partial class DryRunPublisher(SocialPlatform platform, ILogger<DryRunPublisher> logger) : ISocialPublisher
{
    public SocialPlatform Platform => platform;

    public Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken)
    {
        LogDryRun(platform, publication.IdempotencyKey, publication.Announcement.Text);
        return Task.FromResult<string?>(null);
    }

    public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Dry run: not posting {Platform} announcement {Key}: {Text}")]
    private partial void LogDryRun(SocialPlatform platform, string key, string text);
}
