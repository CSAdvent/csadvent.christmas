using CSAdvent.Web.Contributors;
using CSAdvent.Web.Email.Queue;

namespace CSAdvent.Web.Manage;

/// <summary>What's waiting for an organizer, counted for the badges in the organizer area's navigation (#173).</summary>
/// <param name="Requests">The current season's claim requests still waiting, as <c>/manage/requests</c> lists them.</param>
public sealed record AttentionCounts(int Requests, int PostClaims, int LinkChanges, int FailedEmails)
{
    public static readonly AttentionCounts None = new(0, 0, 0, 0);
}

/// <summary>
/// Counts what's waiting for an organizer: a handful of indexed counts on every organizer page. They run in a scope of their
/// own, so with a database context of their own, because the navigation loads while its page is still querying, and a
/// context can't run two queries at once.
/// </summary>
public sealed class OrganizerAttention(IServiceScopeFactory scopes)
{
    public async Task<AttentionCounts> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var year = await services.GetRequiredService<SeasonAdminService>().GetCurrentYearAsync(cancellationToken);
        return new(
            year is { } current ? await services.GetRequiredService<ClaimReviewService>().CountPendingAsync(current, cancellationToken) : 0,
            await services.GetRequiredService<PostClaimService>().CountPendingAsync(cancellationToken),
            await services.GetRequiredService<PostLinkService>().CountPendingAsync(cancellationToken),
            await services.GetRequiredService<EmailOutbox>().CountFailedAsync(cancellationToken));
    }
}
