using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Contributors;

/// <summary>What we know about a contributor that they can't change here: it comes from GitHub.</summary>
public sealed record ProfileAccount(string GithubLogin, string? VerifiedEmail, int Spots);

/// <summary>
/// The parts of a contributor's account they manage themselves (on <c>/profile</c>), and organizers can manage for them: the
/// name and link they're credited with, and their social accounts. Their GitHub login and email come from GitHub, and the
/// social links on their GitHub profile fill in any accounts they haven't given us.
/// </summary>
public sealed class ProfileService(AdventDbContext db, GitHubUsers github, TimeProvider timeProvider, ILogger<ProfileService> logger)
{
    /// <summary>How long to wait for GitHub's list of social links before carrying on without them.</summary>
    public static readonly TimeSpan GitHubTimeout = TimeSpan.FromSeconds(5);

    public async Task<ProfileAccount?> GetAccountAsync(int contributorId, CancellationToken cancellationToken = default) =>
        await db.Contributors
            .AsNoTracking()
            .Where(c => c.Id == contributorId)
            .Select(c => new ProfileAccount(
                c.GithubLogin,
                c.EmailVerifiedAt != null ? c.Email : null,
                db.Slots.Count(s => s.ContributorId == c.Id && s.SeasonYear != Season.TestYear && s.AuthorName != null)))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The profile as saved, or for someone who's never saved one, the name and link on their latest post.</summary>
    public async Task<ProfileForm?> GetAsync(int contributorId, string? fallbackName = null, CancellationToken cancellationToken = default)
    {
        if (await db.Contributors.AsNoTracking().SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken) is not { } contributor)
        {
            return null;
        }

        var latest = await db.Slots
            .AsNoTracking()
            .Where(s => s.ContributorId == contributorId && s.SeasonYear != Season.TestYear && s.AuthorName != null)
            .OrderByDescending(s => s.SeasonYear).ThenByDescending(s => s.Day)
            .Select(s => new { s.AuthorName, s.AuthorUrl })
            .FirstOrDefaultAsync(cancellationToken);

        return new ProfileForm
        {
            Name = contributor.DisplayName ?? latest?.AuthorName ?? fallbackName ?? contributor.GithubLogin,
            // An archive post's link can be a social media profile, which the website field doesn't take.
            Link = contributor.WebsiteUrl ?? (SocialAccounts.IsSocialProfile(latest?.AuthorUrl) ? null : latest?.AuthorUrl),
            Bluesky = contributor.BlueskyHandle,
            Mastodon = contributor.MastodonHandle,
            LinkedIn = contributor.LinkedInUrl,
            Facebook = contributor.FacebookUrl,
            Reddit = contributor.RedditUsername,
        };
    }

    /// <summary>
    /// Saves the profile, which fills in their next claim. With <see cref="ProfileForm.UpdatePosts"/>, it also puts the name
    /// and link on every spot of theirs, past seasons included.
    /// </summary>
    public async Task<bool> SaveAsync(int contributorId, ProfileForm form, CancellationToken cancellationToken = default)
    {
        if (await db.Contributors.SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken) is not { } contributor)
        {
            return false;
        }

        var name = form.Name.Trim();
        var link = string.IsNullOrWhiteSpace(form.Link) ? null : form.Link.Trim();
        contributor.DisplayName = name;
        contributor.WebsiteUrl = link;
        contributor.BlueskyHandle = Normalized(SocialPlatform.Bluesky, form.Bluesky);
        contributor.MastodonHandle = Normalized(SocialPlatform.Mastodon, form.Mastodon);
        contributor.LinkedInUrl = Normalized(SocialPlatform.LinkedIn, form.LinkedIn);
        contributor.FacebookUrl = Normalized(SocialPlatform.Facebook, form.Facebook);
        contributor.RedditUsername = Normalized(SocialPlatform.Reddit, form.Reddit);
        await db.SaveChangesAsync(cancellationToken);

        if (form.UpdatePosts)
        {
            await db.Slots
                .Where(s => s.ContributorId == contributorId && s.SeasonYear != Season.TestYear && s.AuthorName != null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorName, name).SetProperty(x => x.AuthorUrl, link), cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Fills in the accounts missing from their profile from the links on their GitHub profile, on their first sign-in. It's
    /// done once, so an account they remove here doesn't come back. If GitHub is slow or down, the sign-in carries on and
    /// it's tried again next time.
    /// </summary>
    public async Task FillInFromGitHubOnceAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        if (await db.Contributors.SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken) is not { GithubSocialCheckedAt: null } contributor)
        {
            return;
        }

        if (await ReadGitHubAsync(contributor.GithubLogin, cancellationToken) is not { } found)
        {
            return;
        }

        FillIn(contributor, found);
        contributor.GithubSocialCheckedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fills in missing accounts from their GitHub profile when they ask, for links they've added there since. Returns the
    /// platforms it added, or null if GitHub didn't answer.
    /// </summary>
    public async Task<IReadOnlyList<SocialPlatform>?> FillInFromGitHubAsync(int contributorId, CancellationToken cancellationToken = default)
    {
        if (await db.Contributors.SingleOrDefaultAsync(c => c.Id == contributorId, cancellationToken) is not { } contributor)
        {
            return [];
        }

        if (await ReadGitHubAsync(contributor.GithubLogin, cancellationToken) is not { } found)
        {
            return null;
        }

        var added = FillIn(contributor, found);
        contributor.GithubSocialCheckedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Puts the GitHub profile's accounts on our platforms into the contributor's empty fields, never over one they've given
    /// us. Returns the platforms it filled in.
    /// </summary>
    public static IReadOnlyList<SocialPlatform> FillIn(Contributor contributor, IEnumerable<GitHubSocialAccount> found)
    {
        var added = new List<SocialPlatform>();
        foreach (var (platform, account) in found.Select(a => SocialAccounts.FromGitHub(a.Provider, a.Url)).OfType<(SocialPlatform Platform, string Account)>())
        {
            switch (platform)
            {
                case SocialPlatform.Bluesky when contributor.BlueskyHandle is null:
                    contributor.BlueskyHandle = account;
                    break;
                case SocialPlatform.Mastodon when contributor.MastodonHandle is null:
                    contributor.MastodonHandle = account;
                    break;
                case SocialPlatform.LinkedIn when contributor.LinkedInUrl is null:
                    contributor.LinkedInUrl = account;
                    break;
                case SocialPlatform.Facebook when contributor.FacebookUrl is null:
                    contributor.FacebookUrl = account;
                    break;
                case SocialPlatform.Reddit when contributor.RedditUsername is null:
                    contributor.RedditUsername = account;
                    break;
                default:
                    continue;
            }

            added.Add(platform);
        }

        return added;
    }

    private async Task<IReadOnlyList<GitHubSocialAccount>?> ReadGitHubAsync(string login, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GitHubTimeout);
        try
        {
            return await github.SocialAccountsAsync(login, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Without the login: the logs never carry personal details.
            logger.LogWarning(exception, "Couldn't read a contributor's social links from GitHub.");
            return null;
        }
    }

    private static string? Normalized(SocialPlatform platform, string? input) =>
        SocialAccounts.TryNormalize(platform, input, out var normalized) ? normalized : null;
}

/// <summary>The "Fill in from GitHub" button on the profile. It posts nothing but its antiforgery token.</summary>
public sealed class GitHubFillForm;

/// <summary>
/// How a contributor is credited, and their social accounts, checked the way the claim form checks them (with the same limits,
/// because the profile fills in the claim form).
/// </summary>
public sealed class ProfileForm
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(50, ErrorMessage = "Keep the name to 50 characters or fewer.")]
    public string Name { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the link to 200 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Enter a full web address that starts with https:// or http://.")]
    [NotSocialProfile]
    public string? Link { get; set; }

    /// <summary>Also put the name and link on every spot of theirs, not just the next one.</summary>
    public bool UpdatePosts { get; set; }

    [SocialAccount(SocialPlatform.Bluesky, ErrorMessage = "Enter a Bluesky handle, such as you.bsky.social.")]
    public string? Bluesky { get; set; }

    [SocialAccount(SocialPlatform.Mastodon, ErrorMessage = "Enter a Mastodon handle, such as @you@mastodon.social.")]
    public string? Mastodon { get; set; }

    [SocialAccount(SocialPlatform.LinkedIn, ErrorMessage = "Enter a LinkedIn profile URL, such as https://www.linkedin.com/in/you.")]
    public string? LinkedIn { get; set; }

    [SocialAccount(SocialPlatform.Facebook, ErrorMessage = "Enter a Facebook profile or Page URL, such as https://www.facebook.com/you.")]
    public string? Facebook { get; set; }

    [SocialAccount(SocialPlatform.Reddit, ErrorMessage = "Enter a Reddit username, such as u/you.")]
    public string? Reddit { get; set; }
}
