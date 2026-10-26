using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Contributors;

/// <summary>Who a post is linked to, as far as the signed-in contributor is concerned.</summary>
public enum PostOwner
{
    Nobody,
    You,
    SomeoneElse,
}

/// <summary>A finished season's post, as someone looking for their own older posts sees it.</summary>
/// <param name="Requested">They've asked for it, and an organizer hasn't decided yet.</param>
public sealed record ClaimablePost(int SlotId, int Year, int Day, int Part, int SlotsPerDay, string Title, string Author, PostOwner Owner, bool Requested)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    public string EntryPage => EntryLinks.Page(Year, Day, Part);

    public string ClaimPage => PostClaimLinks.Claim(Year, Day, Part);
}

public static class PostClaimLinks
{
    /// <summary>Where a signed-in contributor asks for the post to be added to their account.</summary>
    public static string Claim(int year, int day, int part) => $"my-spots/claim/{year}/{day}/{part}";
}

public enum PostClaimOutcome
{
    Requested,
    AlreadyRequested,

    /// <summary>It's on an account already: theirs or someone else's.</summary>
    AlreadyLinked,
    TooMany,
    NoVerifiedEmail,
    NotFound,
}

public enum PostClaimDecision
{
    Approved,
    Declined,
    AlreadyDecided,
    LinkedToSomeoneElse,
    ChangedByOthers,
    NotFound,
}

/// <summary>A request as the organizers review it.</summary>
/// <param name="Author">The author's name as it's shown on the post.</param>
/// <param name="RequesterPosts">Posts already on the requester's account.</param>
/// <param name="LinkedTo">The GitHub login the post is linked to now, if any.</param>
public sealed record PostClaimReview(
    int Id,
    PostClaimStatus Status,
    uint Version,
    DateTimeOffset SubmittedAt,
    string? Note,
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string Title,
    string PostUrl,
    string Author,
    int RequesterId,
    string RequesterLogin,
    string? RequesterName,
    int RequesterPosts,
    string? LinkedTo,
    DateTimeOffset? DecidedAt,
    string? DecidedBy)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";

    public string EntryPage => EntryLinks.Page(Year, Day, Part);

    public PostClaimEmailDetails Email => new(Id, RequesterLogin, RequesterName, Note, Year, Day, Part, SlotsPerDay, Title, Author);
}

/// <summary>
/// Requests to add a finished season's post to someone's account (#120). A signed-in contributor asks for a post that
/// isn't linked to anyone, every request emails the organizers, and an organizer approves (linking the post) or declines
/// it. A post that's already on someone's account can't be requested: the pages say to email the organizers instead.
/// </summary>
public sealed partial class PostClaimService(
    AdventDbContext db,
    AdventSchedule schedule,
    AuthorService authors,
    TimeProvider timeProvider,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<SiteOptions> site,
    ILogger<PostClaimService> logger)
{
    /// <summary>Requests one contributor can make in an hour, since each one emails the organizers.</summary>
    public const int MaxPerHour = 10;

    public const int MaxResults = 50;

    /// <summary>The post, if it's from a finished season.</summary>
    public async Task<ClaimablePost?> GetAsync(int contributorId, int year, int day, int part, CancellationToken cancellationToken = default)
    {
        if (year == Season.TestYear || !schedule.IsFinished(year))
        {
            return null;
        }

        return (await Claimable(contributorId, Posts.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part))
            .ToListAsync(cancellationToken)).SingleOrDefault();
    }

    /// <summary>Finished seasons' posts that aren't on anyone's account, by an author whose name contains the search.</summary>
    public async Task<IReadOnlyList<ClaimablePost>> SearchAsync(int contributorId, string? search, CancellationToken cancellationToken = default)
    {
        if (search?.Trim() is not { Length: >= 2 } term)
        {
            return [];
        }

        var pattern = $"%{EscapeLike(term)}%";
        var posts = await Claimable(contributorId, Posts.Where(s => s.ContributorId == null && EF.Functions.ILike(s.AuthorName!, pattern, @"\")))
            .ToListAsync(cancellationToken);
        return [.. posts.Where(p => schedule.IsFinished(p.Year)).Take(MaxResults)];
    }

    /// <summary>The contributor's requests an organizer hasn't decided yet.</summary>
    public async Task<IReadOnlyList<ClaimablePost>> GetPendingAsync(int contributorId, CancellationToken cancellationToken = default) =>
        await Claimable(contributorId, Posts.Where(s => db.PostClaimRequests.Any(r => r.SlotId == s.Id && r.ContributorId == contributorId && r.Status == PostClaimStatus.Pending)))
            .ToListAsync(cancellationToken);

    /// <summary>Asks for the post to be added to the contributor's account, and emails the organizers.</summary>
    public async Task<PostClaimOutcome> RequestAsync(int contributorId, int year, int day, int part, string? note, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(contributorId, year, day, part, cancellationToken) is not { } post)
        {
            return PostClaimOutcome.NotFound;
        }

        if (post.Owner is not PostOwner.Nobody)
        {
            return PostClaimOutcome.AlreadyLinked;
        }

        if (post.Requested)
        {
            return PostClaimOutcome.AlreadyRequested;
        }

        var requester = await db.Contributors
            .Where(c => c.Id == contributorId)
            .Select(c => new { c.GithubLogin, c.DisplayName, c.Email, c.EmailVerifiedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (requester is null)
        {
            return PostClaimOutcome.NotFound;
        }

        if (requester.Email is null || requester.EmailVerifiedAt is null)
        {
            return PostClaimOutcome.NoVerifiedEmail;
        }

        var now = timeProvider.GetUtcNow();
        var hourAgo = now.AddHours(-1);
        if (await db.PostClaimRequests.CountAsync(r => r.ContributorId == contributorId && r.SubmittedAt > hourAgo, cancellationToken) >= MaxPerHour)
        {
            return PostClaimOutcome.TooMany;
        }

        var request = new PostClaimRequest
        {
            SlotId = post.SlotId,
            ContributorId = contributorId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            SubmittedAt = now,
            Status = PostClaimStatus.Pending,
        };
        db.PostClaimRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return PostClaimOutcome.AlreadyRequested;
        }

        var details = new PostClaimEmailDetails(request.Id, requester.GithubLogin, requester.DisplayName, request.Note, post.Year, post.Day, post.Part, post.SlotsPerDay, post.Title, post.Author);
        await SendAsync(request.Id, "new request", PostClaimEmails.NewRequest(details, site.Value), new EmailRecipient(site.Value.ContactEmail, "C# Advent organizers"), requester.Email, cancellationToken);
        return PostClaimOutcome.Requested;
    }

    public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        db.PostClaimRequests.CountAsync(r => r.Status == PostClaimStatus.Pending, cancellationToken);

    /// <summary>Requests waiting for an organizer, oldest first.</summary>
    public async Task<IReadOnlyList<PostClaimReview>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await Reviews(db.PostClaimRequests.Where(r => r.Status == PostClaimStatus.Pending).OrderBy(r => r.SubmittedAt)).ToListAsync(cancellationToken);

    public Task<PostClaimReview?> GetReviewAsync(int id, CancellationToken cancellationToken = default) =>
        Reviews(db.PostClaimRequests.Where(r => r.Id == id)).SingleOrDefaultAsync(cancellationToken);

    /// <summary>The finished seasons' other posts under the same name (whatever its case or spacing) that aren't on anyone's account.</summary>
    public async Task<int> CountSameNameAsync(PostClaimReview review, CancellationToken cancellationToken = default)
    {
        var key = AuthorService.Key(review.Author);
        var names = await Posts
            .Where(s => s.ContributorId == null && s.Id != review.SlotId)
            .Select(s => new { s.SeasonYear, s.AuthorName })
            .ToListAsync(cancellationToken);
        return names.Count(p => schedule.IsFinished(p.SeasonYear) && AuthorService.Key(p.AuthorName!) == key);
    }

    /// <summary>
    /// Links the post to the requester and tells them. With <paramref name="alsoSameName"/>, their other unlinked posts under
    /// the same name too, as linking an archive author does (<see cref="AuthorService.CombineAsync"/>).
    /// </summary>
    public async Task<PostClaimDecision> ApproveAsync(int id, uint version, bool alsoSameName, string decidedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PostClaimRequests.Include(r => r.Slot).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PostClaimDecision.NotFound;
        }

        if (request.Status is not PostClaimStatus.Pending)
        {
            return PostClaimDecision.AlreadyDecided;
        }

        if (request.Slot.ContributorId is { } linked && linked != request.ContributorId)
        {
            return PostClaimDecision.LinkedToSomeoneElse;
        }

        // The request's and the post's versions together: a decision or a link in between refuses this one.
        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        request.Status = PostClaimStatus.Approved;
        request.DecidedAt = timeProvider.GetUtcNow();
        request.DecidedBy = decidedBy;
        request.Slot.ContributorId = request.ContributorId;
        if (await SaveDecisionAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        if (alsoSameName && request.Slot.AuthorName is { } author)
        {
            await authors.CombineAsync(request.ContributorId, AuthorService.Key(author), cancellationToken);
        }

        await TellRequesterAsync(id, PostClaimEmails.Approved, cancellationToken);
        return PostClaimDecision.Approved;
    }

    public async Task<PostClaimDecision> DeclineAsync(int id, uint version, string decidedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PostClaimRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PostClaimDecision.NotFound;
        }

        if (request.Status is not PostClaimStatus.Pending)
        {
            return PostClaimDecision.AlreadyDecided;
        }

        db.Entry(request).Property(r => r.Version).OriginalValue = version;
        request.Status = PostClaimStatus.Declined;
        request.DecidedAt = timeProvider.GetUtcNow();
        request.DecidedBy = decidedBy;
        if (await SaveDecisionAsync(cancellationToken) is { } refused)
        {
            return refused;
        }

        await TellRequesterAsync(id, PostClaimEmails.Declined, cancellationToken);
        return PostClaimDecision.Declined;
    }

    /// <summary>Escapes <c>%</c>, <c>_</c> and the escape character itself, so a search only matches what was typed.</summary>
    public static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>Posts from real seasons, with an author and a link. Callers check the season is over.</summary>
    private IQueryable<Slot> Posts => db.Slots.AsNoTracking().Where(s => s.SeasonYear != Season.TestYear && s.PostUrl != null && s.AuthorName != null);

    private IQueryable<ClaimablePost> Claimable(int contributorId, IQueryable<Slot> slots) =>
        slots
            .OrderByDescending(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new ClaimablePost(
                s.Id,
                s.SeasonYear,
                s.Day,
                s.Part,
                s.Season.SlotsPerDay,
                s.PostTitle ?? "Untitled",
                s.AuthorName!,
                s.ContributorId == null ? PostOwner.Nobody : s.ContributorId == contributorId ? PostOwner.You : PostOwner.SomeoneElse,
                db.PostClaimRequests.Any(r => r.SlotId == s.Id && r.ContributorId == contributorId && r.Status == PostClaimStatus.Pending)));

    // Filter and sort the requests before this: EF can't translate a filter on the record it projects to.
    private IQueryable<PostClaimReview> Reviews(IQueryable<PostClaimRequest> requests) =>
        requests
            .AsNoTracking()
            .Select(r => new PostClaimReview(
                r.Id,
                r.Status,
                r.Version,
                r.SubmittedAt,
                r.Note,
                r.SlotId,
                r.Slot.SeasonYear,
                r.Slot.Day,
                r.Slot.Part,
                r.Slot.Season.SlotsPerDay,
                r.Slot.PostTitle ?? "Untitled",
                r.Slot.PostUrl ?? "",
                r.Slot.AuthorName ?? "",
                r.ContributorId,
                r.Contributor.GithubLogin,
                r.Contributor.DisplayName,
                db.Slots.Count(s => s.ContributorId == r.ContributorId && s.PostUrl != null),
                r.Slot.Contributor != null ? r.Slot.Contributor.GithubLogin : null,
                r.DecidedAt,
                r.DecidedBy));

    private async Task<PostClaimDecision?> SaveDecisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PostClaimDecision.ChangedByOthers;
        }
    }

    private async Task TellRequesterAsync(int id, Func<PostClaimEmailDetails, SiteOptions, EmailContent> email, CancellationToken cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't queue the {Kind} email for post claim request {RequestId}")]
    private partial void LogSendFailed(Exception exception, string kind, int requestId);
}

/// <summary>Asking for an older post: a note for the organizers, and a ticked "I wrote this".</summary>
public sealed class PostClaimForm
{
    [StringLength(1000, ErrorMessage = "Keep your note to 1,000 characters or fewer.")]
    public string? Note { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm you wrote this post.")]
    public bool IWroteIt { get; set; }
}

/// <summary>An organizer's decision on a request.</summary>
public sealed class PostClaimDecisionForm
{
    /// <summary>Also link the requester's other unlinked posts under the same name.</summary>
    public bool AlsoSameName { get; set; }

    /// <summary>The request's row version when the page was shown.</summary>
    public uint Version { get; set; }
}
