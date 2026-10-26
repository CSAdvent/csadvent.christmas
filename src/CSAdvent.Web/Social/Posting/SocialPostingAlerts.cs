using System.Globalization;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Social.Posting;

/// <summary>A post automatic posting has stopped on, waiting for an organizer, as the email describes it.</summary>
/// <param name="Name">Which post: "C# Advent 2026, December 6, post 2", or "the C# Advent 2026 “sign-ups are open” post".</param>
/// <param name="Title">The entry's title, for an entry's announcement.</param>
/// <param name="Page">Where it's posted from, below the site: <c>manage/social?year=2026&amp;day=6</c>.</param>
/// <param name="Retry">The button there that tries it again: Retry now, or Post now.</param>
/// <param name="MayHavePosted">It's Unconfirmed: it may have gone out, so it's checked before it's tried again.</param>
/// <param name="Attempts">How many times it was tried, if it gave up after failing.</param>
public sealed record StoppedPost(SocialPlatform Platform, string Name, string? Title, string Page, string Retry, bool MayHavePosted, string Error, int Attempts)
{
    /// <summary>An entry's announcement.</summary>
    public static StoppedPost Entry(SocialPlatform platform, int year, int day, int part, int slotsPerDay, string? title, bool mayHavePosted, string error, int attempts) =>
        new(
            platform,
            slotsPerDay > 1
                ? string.Create(CultureInfo.InvariantCulture, $"C# Advent {year}, December {day}, post {part}")
                : string.Create(CultureInfo.InvariantCulture, $"C# Advent {year}, December {day}"),
            title,
            string.Create(CultureInfo.InvariantCulture, $"manage/social?year={year}&day={day}"),
            "Retry now",
            mayHavePosted,
            error,
            attempts);

    /// <summary>A season post: the sign-ups posts, from the sign-ups page, or the retrospective, from its page.</summary>
    public static StoppedPost Season(SocialPlatform platform, int year, SeasonPostKind kind, bool mayHavePosted, string error) =>
        new(
            platform,
            kind switch
            {
                SeasonPostKind.SignUpsSoon => string.Create(CultureInfo.InvariantCulture, $"the C# Advent {year} “sign-ups open soon” post"),
                SeasonPostKind.SignUpsOpen => string.Create(CultureInfo.InvariantCulture, $"the C# Advent {year} “sign-ups are open” post"),
                _ => string.Create(CultureInfo.InvariantCulture, $"the C# Advent {year} retrospective post"),
            },
            null,
            kind is SeasonPostKind.Retrospective
                ? string.Create(CultureInfo.InvariantCulture, $"manage/retrospectives/{year}")
                : string.Create(CultureInfo.InvariantCulture, $"manage/social/signups?year={year}"),
            "Post now",
            mayHavePosted,
            error,
            1);
}

/// <summary>
/// Emails the organizers when automatic posting stops on a post and waits for them (#161): it may have gone out without the
/// platform saying so (Unconfirmed, including a post a restart interrupted), or it failed its last automatic try. Otherwise
/// they'd only find out by looking at the social page. Not for a try an organizer made themselves, which shows on their page.
/// </summary>
public sealed class SocialPostingAlerts(EmailComposer composer, IOptions<SiteOptions> site)
{
    private const string ForOrganizers = "You're getting this email because you're a C# Advent organizer.";

    /// <summary>Tells the organizers. If it can't be queued, the post's row says what happened anyway, on the social page.</summary>
    public Task StoppedAsync(StoppedPost post, CancellationToken cancellationToken) =>
        composer.TrySendAsync(Stopped(post, site.Value), site.Value.OrganizersInbox, $"posting to {post.Platform}", cancellationToken);

    /// <summary>The email: <see cref="Unconfirmed"/> or <see cref="GaveUp"/>.</summary>
    public static EmailContent Stopped(StoppedPost post, SiteOptions site) => post.MayHavePosted ? Unconfirmed(post, site) : GaveUp(post, site);

    /// <summary>It may have gone out: check the account, then mark it posted or try again.</summary>
    public static EmailContent Unconfirmed(StoppedPost post, SiteOptions site) => new()
    {
        Kind = "social-post-unconfirmed",
        Subject = $"Did {post.Name} go out on {post.Platform}?",
        Preview = $"{post.Platform} never said whether it went out, so it won't be tried again until you've checked.",
        Heading = $"Did it go out on {post.Platform}?",
        Body =
        [
            Paragraph(
                $"C# Advent tried to post {What(post)} to {post.Platform}, but {post.Platform} never said whether it went out, so it " +
                "may have. It won't be tried again by itself, because that could post it twice."),
            Details(new DetailRow("What happened", EmailBlocks.SingleLine(post.Error))),
            Paragraph(
                $"Look for it on C# Advent's {post.Platform} account. If it's there, press Mark as posted (with its link, if you " +
                $"like). If it isn't, press {post.Retry}."),
            Button("Open the page", Page(post, site)),
        ],
        Reason = ForOrganizers,
    };

    /// <summary>It failed its last automatic try: fix what's wrong, then try again or post it by hand.</summary>
    public static EmailContent GaveUp(StoppedPost post, SiteOptions site) => new()
    {
        Kind = "social-post-gave-up",
        Subject = $"{post.Platform} didn't post {post.Name}",
        Preview = "Automatic posting has stopped trying, and it's waiting for you.",
        Heading = $"{post.Platform} didn't post it",
        Body =
        [
            Paragraph(post.Attempts > 1
                ? string.Create(CultureInfo.InvariantCulture, $"C# Advent tried {post.Attempts} times to post {What(post)} to {post.Platform}, and it didn't go out, so it's stopped trying.")
                : $"C# Advent tried to post {What(post)} to {post.Platform}, and it didn't go out. It won't be tried again by itself."),
            Details(new DetailRow("What happened", EmailBlocks.SingleLine(post.Error))),
            Paragraph(
                "If it's something on our side, fix it first: for the credentials, docs/deployment.md in the repository says how to replace " +
                $"them. Then press {post.Retry}, or post it by hand and press Mark as posted."),
            Button("Open the page", Page(post, site)),
        ],
        Reason = ForOrganizers,
    };

    private static string What(StoppedPost post) => post.Title is { } title ? $"{post.Name} (“{title}”)" : post.Name;

    private static string Page(StoppedPost post, SiteOptions site) => $"{site.BaseUrl.TrimEnd('/')}/{post.Page}";
}
