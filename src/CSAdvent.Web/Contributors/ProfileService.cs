using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Contributors;

/// <summary>What we know about a contributor that they can't change here: it comes from GitHub.</summary>
public sealed record ProfileAccount(string GithubLogin, string? VerifiedEmail, int Spots);

/// <summary>
/// The parts of a contributor's account they manage themselves (on <c>/profile</c>), and organizers can manage for them: the
/// name and link they're credited with, and their social accounts. Their GitHub login and email come from GitHub.
/// </summary>
public sealed class ProfileService(AdventDbContext db)
{
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

    private static string? Normalized(SocialPlatform platform, string? input) =>
        SocialAccounts.TryNormalize(platform, input, out var normalized) ? normalized : null;
}

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
