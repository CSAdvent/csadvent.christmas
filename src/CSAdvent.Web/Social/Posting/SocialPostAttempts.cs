using System.Text.Json;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Social.Posting;

/// <summary>How one try at a post went: posted (with its address, if the platform gave one), or not, and why.</summary>
/// <param name="MayHavePosted">It failed once the post had been sent, so it may have gone out anyway (<see cref="SocialPublishException.MayHavePosted"/>).</param>
/// <param name="Exception">What failed, for the log.</param>
public sealed record PostAttempt(string? PostUrl, string? Error, bool MayHavePosted = false, Exception? Exception = null)
{
    public bool Posted => Error is null;
}

/// <summary>
/// Posting, the same way for entries' announcements (<see cref="SocialPostingService"/>) and season posts
/// (<see cref="SeasonSocialService"/>), #187:
/// <list type="bullet">
/// <item>a run claims a post by its row (<see cref="ISocialPostRecord"/>, status Posting) before posting it, so only one run or
/// replica posts it;</item>
/// <item>one try at the platform, and what a failure means (<see cref="StatusAfter"/>);</item>
/// <item>the result is recorded only while the row is still that run's claim;</item>
/// <item>a row still posting long after its run was interrupted, and waits for an organizer.</item>
/// </list>
/// </summary>
public static class SocialPostAttempts
{
    /// <summary>A post still marked as posting after this long was interrupted (a restart, say), and won't be tried again by itself.</summary>
    public static readonly TimeSpan InterruptedAfter = TimeSpan.FromMinutes(15);

    /// <summary>What an interrupted post's row says went wrong.</summary>
    public const string Interrupted = "Interrupted while posting (the app restarted, say).";

    /// <summary>Posts it once. Any failure the platform causes is returned rather than thrown; the app stopping is thrown.</summary>
    public static async Task<PostAttempt> TryAsync(ISocialPublisher publisher, SocialPublication publication, CancellationToken cancellationToken)
    {
        try
        {
            return new PostAttempt(SafeUrl(await publisher.PublishAsync(publication, cancellationToken)), null);
        }
        catch (Exception exception) when (IsPlatformFailure(exception, cancellationToken))
        {
            return new PostAttempt(null, Describe(exception, publisher.Platform), exception is SocialPublishException { MayHavePosted: true }, exception);
        }
    }

    /// <summary>A refusal, an unreadable answer, a lost connection or a timeout: anything a platform causes, but not the app stopping.</summary>
    public static bool IsPlatformFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is SocialPublishException or HttpRequestException or JsonException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>What went wrong, for the organizers, short enough for the error column.</summary>
    public static string Describe(Exception exception, SocialPlatform platform)
    {
        var error = exception is TaskCanceledException ? $"{platform} didn't answer in time." : exception.Message;
        return error[..Math.Min(error.Length, 1000)];
    }

    /// <summary>
    /// What a try leaves its row as. A failure is Failed: automatic posting tries an entry's announcement again a few times,
    /// and an organizer can retry either kind. A failure that may have gone out is Unconfirmed instead, waiting for an
    /// organizer to check, unless trying again can't post it twice (<see cref="ISocialPublisher.RetriesAreSafe"/>).
    /// </summary>
    public static SocialPostStatus StatusAfter(PostAttempt attempt, bool retriesAreSafe) =>
        attempt.Posted ? SocialPostStatus.Posted
        : attempt.MayHavePosted && !retriesAreSafe ? SocialPostStatus.Unconfirmed
        : SocialPostStatus.Failed;

    /// <summary>The post's address, if it's a web address (the column only takes those).</summary>
    public static string? SafeUrl(string? postUrl) =>
        Uri.TryCreate(postUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? postUrl : null;

    /// <summary>
    /// Claims a post with a new row (status Posting). Returns false if another run, or an organizer, got there first: the
    /// table's unique index refuses a second row.
    /// </summary>
    public static async Task<bool> TryClaimAsync<T>(AdventDbContext db, T claim, CancellationToken cancellationToken)
        where T : class, ISocialPostRecord
    {
        db.Add(claim);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// Records how a claimed try went, only while the row is still this run's claim: an organizer may have marked it posted
    /// by hand in the meantime. Returns whether it did.
    /// </summary>
    /// <param name="post">The claimed row.</param>
    public static async Task<bool> RecordAsync<T>(IQueryable<T> post, SocialPostStatus status, string? postUrl, string? error, DateTimeOffset at, CancellationToken cancellationToken)
        where T : class, ISocialPostRecord =>
        await post
            .Where(p => p.Status == SocialPostStatus.Posting)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.PostUrl, postUrl)
                .SetProperty(p => p.Error, error)
                .SetProperty(p => p.At, at), cancellationToken) > 0;

    /// <summary>
    /// A post still marked as posting long after its run (the app restarted mid-post, say) may or may not have gone out, so
    /// it's marked Unconfirmed for an organizer to check, and never tried again by itself. Returns the rows this call marked,
    /// as they were: each is moved by one run only, so replicas don't both report it.
    /// </summary>
    /// <param name="now">The real time: a restart happens in real time, whatever time a development run pretends it is.</param>
    public static async Task<IReadOnlyList<T>> GiveUpOnInterruptedAsync<T>(IQueryable<T> posts, DateTimeOffset now, CancellationToken cancellationToken)
        where T : class, ISocialPostRecord
    {
        var cutoff = now - InterruptedAfter;
        var interrupted = await posts.AsNoTracking().Where(p => p.Status == SocialPostStatus.Posting && p.At < cutoff).ToListAsync(cancellationToken);

        var marked = new List<T>();
        foreach (var post in interrupted)
        {
            if (await posts
                .Where(p => p.Id == post.Id && p.Status == SocialPostStatus.Posting && p.At < cutoff)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, SocialPostStatus.Unconfirmed)
                    .SetProperty(p => p.Error, Interrupted), cancellationToken) > 0)
            {
                marked.Add(post);
            }
        }

        return marked;
    }
}
