using System.Text.Json;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Social.Posting;

/// <summary>How one platform's announcements go out, for the social page.</summary>
/// <param name="CanPostAutomatically">It has a publisher: credentials, or dry-run mode.</param>
/// <param name="IsDryRun">Automatic posts are only logged, because there are no credentials (development and the tests).</param>
public sealed record PlatformPosting(
    SocialPlatform Platform,
    SocialPostingMode Mode,
    bool CanPostAutomatically,
    bool IsDryRun,
    DateTimeOffset? CheckedAt,
    string? CheckError)
{
    /// <summary>Automatic, and still able to be: a platform whose credentials were removed goes back to copy and paste.</summary>
    public bool IsAutomatic => Mode is SocialPostingMode.Automatic && CanPostAutomatically;
}

/// <summary>
/// Each platform's posting mode, which organizers change on the social page without a redeploy, and checks of the
/// credentials automatic posting uses.
/// </summary>
public sealed class SocialPlatformModes(AdventDbContext db, SocialPublishers publishers, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<PlatformPosting>> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.SocialPlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Platform, cancellationToken);
        return [.. Enum.GetValues<SocialPlatform>().Select(platform =>
        {
            var setting = settings.GetValueOrDefault(platform);
            var publisher = publishers.For(platform);
            return new PlatformPosting(
                platform,
                setting?.Mode ?? SocialPostingMode.Manual,
                publisher is not null,
                publisher is DryRunPublisher,
                setting?.CheckedAt,
                setting?.CheckError);
        })];
    }

    /// <summary>Changes a platform's mode. Returns false for Automatic on a platform that can't post automatically.</summary>
    public async Task<bool> SetAsync(SocialPlatform platform, SocialPostingMode mode, string updatedBy, CancellationToken cancellationToken = default)
    {
        if (mode is SocialPostingMode.Automatic && publishers.For(platform) is null)
        {
            return false;
        }

        var setting = await FindOrAddAsync(platform, updatedBy, cancellationToken);
        setting.Mode = mode;
        setting.UpdatedAt = timeProvider.GetUtcNow();
        setting.UpdatedBy = updatedBy;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Signs in to the platform with its credentials and records the result. Returns the platform's error, or null if they
    /// work. A platform without credentials can't be checked.
    /// </summary>
    public async Task<string?> CheckAsync(SocialPlatform platform, string checkedBy, CancellationToken cancellationToken = default)
    {
        if (publishers.For(platform) is not { } publisher)
        {
            return "There are no credentials for it.";
        }

        string? error = null;
        try
        {
            await publisher.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is SocialPublishException or HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            error = exception is TaskCanceledException ? "It didn't answer in time." : exception.Message;
        }

        var setting = await FindOrAddAsync(platform, checkedBy, cancellationToken);
        setting.CheckedAt = timeProvider.GetUtcNow();
        setting.CheckError = error is null ? null : error[..Math.Min(error.Length, 1000)];
        await db.SaveChangesAsync(cancellationToken);
        return error;
    }

    private async Task<SocialPlatformSetting> FindOrAddAsync(SocialPlatform platform, string by, CancellationToken cancellationToken)
    {
        if (await db.SocialPlatformSettings.SingleOrDefaultAsync(s => s.Platform == platform, cancellationToken) is { } setting)
        {
            return setting;
        }

        setting = new SocialPlatformSetting { Platform = platform, Mode = SocialPostingMode.Manual, UpdatedAt = timeProvider.GetUtcNow(), UpdatedBy = by };
        db.SocialPlatformSettings.Add(setting);
        return setting;
    }
}
