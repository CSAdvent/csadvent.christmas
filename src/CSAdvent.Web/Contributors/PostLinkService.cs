using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Contributors;

/// <summary>A published post and its link, for changing the link.</summary>
/// <param name="Version">The spot's row version, so an organizer's change can't overwrite one made in between.</param>
public sealed record LinkedPost(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string Title, string PostUrl, uint Version)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    public string EntryPage => EntryLinks.Page(Year, Day, Part);
}

/// <summary>One post's new link in a request.</summary>
/// <param name="OldUrl">Its link when they asked.</param>
/// <param name="CurrentUrl">Its link now.</param>
public sealed record PostLinkChangeView(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string Title, string OldUrl, string NewUrl, string CurrentUrl)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    public string EntryPage => EntryLinks.Page(Year, Day, Part);

    /// <summary>The link was changed some other way since they asked, such as by an organizer.</summary>
    public bool ChangedSince => CurrentUrl != OldUrl && CurrentUrl != NewUrl;

    public PostLinkEmailChange Email => new(Year, Day, Part, SlotsPerDay, Title, OldUrl, NewUrl);
}

/// <summary>A request as its contributor and the organizers see it.</summary>
/// <param name="LinkedElsewhere">Its posts that aren't on the requester's account any more, so it can't be approved.</param>
public sealed record PostLinkReview(
    int Id,
    PostLinkRequestStatus Status,
    uint Version,
    DateTimeOffset SubmittedAt,
    string? Note,
    int RequesterId,
    string RequesterLogin,
    string? RequesterName,
    IReadOnlyList<PostLinkChangeView> Changes,
    int LinkedElsewhere,
    DateTimeOffset? DecidedAt,
    string? DecidedBy)
{
    public PostLinkEmailDetails Email => new(Id, RequesterLogin, RequesterName, Note, [.. Changes.Select(c => c.Email)]);
}

public static class PostLinkLinks
{
    /// <summary>Where a signed-in contributor asks for their older posts' links to be changed.</summary>
    public const string Request = "my-spots/links";

    public const string Requests = "manage/authors/link-changes";

    public static string Review(int id) => $"{Requests}/{id}";
}

public enum PostLinkOutcome
{
    Requested,
    Withdrawn,

    /// <summary>They have a request waiting already, and only have one at a time.</summary>
    AlreadyRequested,

    /// <summary>Every link is the same as it was.</summary>
    NothingChanged,
    TooMany,
    NoVerifiedEmail,

    /// <summary>No posts to change, or (when withdrawing) no request waiting.</summary>
    NotFound,
}

public enum PostLinkDecision
{
    Approved,
    Declined,
    AlreadyDecided,

    /// <summary>One of its posts isn't on the requester's account any more.</summary>
    NotTheirs,
    ChangedByOthers,
    NotFound,
}

public enum PostLinkSaveOutcome
{
    Saved,
    NothingChanged,
    ChangedByOthers,
}

/// <summary>
/// Changing the links of posts from finished seasons, for someone who moved their blog or a post. A signed-in contributor asks
/// for new links to any of the posts on their account (one request, however many posts), the organizers are emailed, and an
/// organizer checks the new links and approves the request (changing them all) or declines it. Organizers can also change an
/// author's links themselves. The calendar, the archive and entry pages link through <c>/go</c>, so a new link applies
/// everywhere at once, including in the emails, social posts and feeds we've sent since 2026.
/// </summary>
/// <remarks>Posts in a season that isn't over are changed on My spots instead (<see cref="MySpotsService"/>).</remarks>
public sealed partial class PostLinkService(
    AdventDbContext db,
    AdventSchedule schedule,
    TimeProvider timeProvider,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<SiteOptions> site,
    ILogger<PostLinkService> logger)
{
    /// <summary>Requests one contributor can make in an hour, since each one emails the organizers.</summary>
    public const int MaxPerHour = 5;

    /// <summary>Their posts from finished seasons, newest season first: their C# Advent history.</summary>
    public async Task<IReadOnlyList<LinkedPost>> GetPostsAsync(int contributorId, CancellationToken cancellationToken = default) =>
        [.. (await PostsAsync(db.Slots.Where(s => s.ContributorId == contributorId), cancellationToken)).Where(p => schedule.IsSeasonOver(p.Year))];

    /// <summary>The posts with links among the spots, in any real season, for an organizer changing an author's links.</summary>
    public Task<List<LinkedPost>> GetPostsAsync(IReadOnlyCollection<int> slotIds, CancellationToken cancellationToken = default) =>
        PostsAsync(db.Slots.Where(s => slotIds.Contains(s.Id)), cancellationToken);

    /// <summary>The contributor's request waiting for an organizer, if they have one.</summary>
    public async Task<PostLinkReview?> GetPendingAsync(int contributorId, CancellationToken cancellationToken = default) =>
        await Reviews(db.PostLinkRequests.Where(r => r.ContributorId == contributorId && r.Status == PostLinkRequestStatus.Pending))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Asks for the changed links on the contributor's posts, and emails the organizers.</summary>
    public async Task<PostLinkOutcome> RequestAsync(int contributorId, PostLinksForm form, CancellationToken cancellationToken = default)
    {
        var posts = await GetPostsAsync(contributorId, cancellationToken);
        if (posts.Count == 0)
        {
            return PostLinkOutcome.NotFound;
        }

        if (await db.PostLinkRequests.AnyAsync(r => r.ContributorId == contributorId && r.Status == PostLinkRequestStatus.Pending, cancellationToken))
        {
            return PostLinkOutcome.AlreadyRequested;
        }

        var changes = Changes(posts, form);
        if (changes.Count == 0)
        {
            return PostLinkOutcome.NothingChanged;
        }

        var requester = await db.Contributors
            .Where(c => c.Id == contributorId)
            .Select(c => new { c.GithubLogin, c.DisplayName, c.Email, c.EmailVerifiedAt })
            .SingleAsync(cancellationToken);
        if (requester.Email is null || requester.EmailVerifiedAt is null)
        {
            return PostLinkOutcome.NoVerifiedEmail;
        }

        var now = timeProvider.GetUtcNow();
        var hourAgo = now.AddHours(-1);
        if (await db.PostLinkRequests.CountAsync(r => r.ContributorId == contributorId && r.SubmittedAt > hourAgo, cancellationToken) >= MaxPerHour)
        {
            return PostLinkOutcome.TooMany;
        }

        var request = new PostLinkRequest
        {
            ContributorId = contributorId,
            Note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim(),
            SubmittedAt = now,
            Status = PostLinkRequestStatus.Pending,
            Changes = [.. changes.Select(c => new PostLinkChange { SlotId = c.Post.SlotId, OldUrl = c.Post.PostUrl, NewUrl = c.NewUrl })],
        };
        db.PostLinkRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return PostLinkOutcome.AlreadyRequested;
        }

        var details = new PostLinkEmailDetails(
            request.Id,
            requester.GithubLogin,
            requester.DisplayName,
            request.Note,
            [.. changes.Select(c => new PostLinkEmailChange(c.Post.Year, c.Post.Day, c.Post.Part, c.Post.SlotsPerDay, c.Post.Title, c.Post.PostUrl, c.NewUrl))]);
        await SendAsync(request.Id, "new request", PostLinkEmails.NewRequest(details, site.Value), new EmailRecipient(site.Value.ContactEmail, "C# Advent organizers"), requester.Email, cancellationToken);
        return PostLinkOutcome.Requested;
    }

    /// <summary>Takes back the contributor's request before an organizer decides it, so they can fix it and ask again.</summary>
    public async Task<PostLinkOutcome> WithdrawAsync(int contributorId, uint version, CancellationToken cancellationToken = default)
    {
        var request = await db.PostLinkRequests.SingleOrDefaultAsync(r => r.ContributorId == contributorId && r.Status == PostLinkRequestStatus.Pending, cancellationToken);
        if (request is null)
        {
            return PostLinkOutcome.NotFound;
        }

        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        request.Status = PostLinkRequestStatus.Withdrawn;
        request.DecidedAt = timeProvider.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return PostLinkOutcome.Withdrawn;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PostLinkOutcome.NotFound;
        }
    }

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        db.PostLinkRequests.CountAsync(r => r.Status == PostLinkRequestStatus.Pending, cancellationToken);

    /// <summary>Requests waiting for an organizer, oldest first.</summary>
    public async Task<IReadOnlyList<PostLinkReview>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await Reviews(db.PostLinkRequests.Where(r => r.Status == PostLinkRequestStatus.Pending).OrderBy(r => r.SubmittedAt)).ToListAsync(cancellationToken);

    public Task<PostLinkReview?> GetReviewAsync(int id, CancellationToken cancellationToken = default) =>
        Reviews(db.PostLinkRequests.Where(r => r.Id == id)).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Changes every post in the request to its new link, and tells the requester.</summary>
    public async Task<PostLinkDecision> ApproveAsync(int id, uint version, string decidedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PostLinkRequests.Include(r => r.Changes).ThenInclude(c => c.Slot).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PostLinkDecision.NotFound;
        }

        if (request.Status is not PostLinkRequestStatus.Pending)
        {
            return PostLinkDecision.AlreadyDecided;
        }

        if (request.Changes.Any(c => c.Slot.ContributorId != request.ContributorId))
        {
            return PostLinkDecision.NotTheirs;
        }

        // The request's version, as the organizer saw it: a decision or a withdrawal in between refuses this one.
        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        request.Status = PostLinkRequestStatus.Approved;
        request.DecidedAt = timeProvider.GetUtcNow();
        request.DecidedBy = decidedBy;
        foreach (var change in request.Changes)
        {
            change.Slot.PostUrl = change.NewUrl;
        }

        if (await SaveDecisionAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        await TellRequesterAsync(id, PostLinkEmails.Approved, cancellationToken);
        return PostLinkDecision.Approved;
    }

    public async Task<PostLinkDecision> DeclineAsync(int id, uint version, string decidedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PostLinkRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PostLinkDecision.NotFound;
        }

        if (request.Status is not PostLinkRequestStatus.Pending)
        {
            return PostLinkDecision.AlreadyDecided;
        }

        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        request.Status = PostLinkRequestStatus.Declined;
        request.DecidedAt = timeProvider.GetUtcNow();
        request.DecidedBy = decidedBy;
        if (await SaveDecisionAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        await TellRequesterAsync(id, PostLinkEmails.Declined, cancellationToken);
        return PostLinkDecision.Declined;
    }

    /// <summary>
    /// An organizer changing the links themselves, on an author's page: only among <paramref name="posts"/>, and each refused
    /// if the spot changed since the page was shown.
    /// </summary>
    public async Task<PostLinkSaveOutcome> SaveAsync(IReadOnlyList<LinkedPost> posts, PostLinksForm form, CancellationToken cancellationToken = default)
    {
        var changes = Changes(posts, form);
        if (changes.Count == 0)
        {
            return PostLinkSaveOutcome.NothingChanged;
        }

        var slotIds = changes.Select(c => c.Post.SlotId).ToList();
        var slots = await db.Slots.Where(s => slotIds.Contains(s.Id)).ToListAsync(cancellationToken);
        foreach (var (post, newUrl) in changes)
        {
            var slot = slots.Single(s => s.Id == post.SlotId);

            // The version the organizer saw, posted back with each link.
            db.Entry(slot).Property(s => s.Version).OriginalValue = form.Posts.First(p => p.SlotId == post.SlotId).Version;
            slot.PostUrl = newUrl;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return PostLinkSaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PostLinkSaveOutcome.ChangedByOthers;
        }
    }

    /// <summary>
    /// The links in the form that differ from the post's, trimmed. Anything that isn't one of the posts is ignored, so a form
    /// can't change someone else's post, and so is an empty link: the form's validation refuses those first.
    /// </summary>
    public static IReadOnlyList<(LinkedPost Post, string NewUrl)> Changes(IReadOnlyList<LinkedPost> posts, PostLinksForm form)
    {
        var byId = posts.ToDictionary(p => p.SlotId);
        return [.. form.Posts
            .Where(field => !string.IsNullOrWhiteSpace(field.Url))
            .DistinctBy(field => field.SlotId)
            .Select(field => (Post: byId.GetValueOrDefault(field.SlotId), NewUrl: field.Url!.Trim()))
            .Where(change => change.Post is not null && change.NewUrl != change.Post.PostUrl)
            .Select(change => (change.Post!, change.NewUrl))];
    }

    /// <summary>Published posts from real seasons. Callers check whether the season needs to be over.</summary>
    private static async Task<List<LinkedPost>> PostsAsync(IQueryable<Slot> slots, CancellationToken cancellationToken) =>
        await slots
            .AsNoTracking()
            .Where(s => s.SeasonYear != Season.TestYear && s.PostUrl != null && s.AuthorName != null)
            .OrderByDescending(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new LinkedPost(s.Id, s.SeasonYear, s.Day, s.Part, s.Season.SlotsPerDay, s.PostTitle ?? "Untitled", s.PostUrl!, s.Version))
            .ToListAsync(cancellationToken);

    // Filter and sort the requests before this: EF can't translate a filter on the record it projects to.
    private IQueryable<PostLinkReview> Reviews(IQueryable<PostLinkRequest> requests) =>
        requests
            .AsNoTracking()
            .Select(r => new PostLinkReview(
                r.Id,
                r.Status,
                r.Version,
                r.SubmittedAt,
                r.Note,
                r.ContributorId,
                r.Contributor.GithubLogin,
                r.Contributor.DisplayName,
                r.Changes
                    .OrderByDescending(c => c.Slot.SeasonYear).ThenBy(c => c.Slot.Day).ThenBy(c => c.Slot.Part)
                    .Select(c => new PostLinkChangeView(
                        c.SlotId,
                        c.Slot.SeasonYear,
                        c.Slot.Day,
                        c.Slot.Part,
                        c.Slot.Season.SlotsPerDay,
                        c.Slot.PostTitle ?? "Untitled",
                        c.OldUrl,
                        c.NewUrl,
                        c.Slot.PostUrl ?? ""))
                    .ToList(),
                r.Changes.Count(c => c.Slot.ContributorId != r.ContributorId),
                r.DecidedAt,
                r.DecidedBy));

    private async Task<PostLinkDecision?> SaveDecisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PostLinkDecision.ChangedByOthers;
        }
    }

    private async Task TellRequesterAsync(int id, Func<PostLinkEmailDetails, SiteOptions, EmailContent> email, CancellationToken cancellationToken)
    {
        if (await GetReviewAsync(id, cancellationToken) is not { } review)
        {
            return;
        }

        var address = await db.Contributors
            .Where(c => c.Id == review.RequesterId && c.EmailVerifiedAt != null)
            .Select(c => c.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (address is not null)
        {
            await SendAsync(id, review.Status.ToString().ToLowerInvariant(), email(review.Email, site.Value), new EmailRecipient(address, review.RequesterName), null, cancellationToken);
        }
    }

    private async Task SendAsync(int requestId, string kind, EmailContent content, EmailRecipient to, string? replyTo, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(await composer.ComposeAsync(content, to, replyTo), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogSendFailed(exception, kind, requestId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the {Kind} email for post link request {RequestId}")]
    private partial void LogSendFailed(Exception exception, string kind, int requestId);
}

/// <summary>New links for posts: one field for each post, filled in with its link, and a note for the organizers.</summary>
public sealed class PostLinksForm : IValidatableObject
{
    private List<PostLinkField> _posts = [];

    /// <summary>Never null: Blazor's form binding sets it to null when a post has none of its fields.</summary>
    public List<PostLinkField> Posts
    {
        get => _posts;
        set => _posts = value ?? [];
    }

    [StringLength(1000, ErrorMessage = "Keep your note to 1,000 characters or fewer.")]
    public string? Note { get; set; }

    /// <summary>A form showing the posts' links as they are.</summary>
    public static PostLinksForm For(IEnumerable<LinkedPost> posts) =>
        new() { Posts = [.. posts.Select(p => new PostLinkField { SlotId = p.SlotId, Url = p.PostUrl, Version = p.Version })] };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var url = new HttpUrlAttribute();

        // Each problem once, however many links have it.
        var problems = new HashSet<string>();
        foreach (var field in Posts)
        {
            var link = field.Url?.Trim();
            if (string.IsNullOrEmpty(link))
            {
                problems.Add("Every post needs a link. Put back the one you emptied, or the post's new address.");
            }
            else if (link.Length > 500)
            {
                problems.Add("Keep each link to 500 characters or fewer.");
            }
            else if (!url.IsValid(link))
            {
                problems.Add($"“{link}” isn't a full web address. Links start with https:// or http://.");
            }
        }

        return problems.Select(problem => new ValidationResult(problem, [nameof(Posts)]));
    }
}

/// <summary>Approving, declining or withdrawing a request.</summary>
public sealed class PostLinkDecisionForm
{
    /// <summary>The request's row version when the page was shown.</summary>
    public uint Version { get; set; }
}

/// <summary>One post's link in a <see cref="PostLinksForm"/>.</summary>
public sealed class PostLinkField
{
    public int SlotId { get; set; }

    public string? Url { get; set; }

    /// <summary>The spot's row version when the form was shown, for organizers changing it directly.</summary>
    public uint Version { get; set; }
}
