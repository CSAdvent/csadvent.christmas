using System.Security.Claims;

namespace CSAdvent.Web.Auth;

/// <summary>
/// The claims in a signed-in user's cookie: the GitHub user ID (<see cref="ClaimTypes.NameIdentifier"/>), the GitHub login
/// (<see cref="ClaimTypes.Name"/>), the verified email if there is one, and the contributor's ID in our database.
/// </summary>
public static class AdventClaims
{
    public const string ContributorId = "csadvent:contributor_id";

    /// <summary>The name on the contributor's GitHub profile, if they've set one.</summary>
    public const string GitHubName = "csadvent:github_name";

    public static IEnumerable<Claim> For(long githubUserId, string login, string? verifiedEmail, int contributorId, string? name = null)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, githubUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new Claim(ClaimTypes.Name, login);
        yield return new Claim(ContributorId, contributorId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (verifiedEmail is not null)
        {
            yield return new Claim(ClaimTypes.Email, verifiedEmail);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            yield return new Claim(GitHubName, name.Trim());
        }
    }

    /// <summary>The GitHub profile name if there is one, otherwise the login.</summary>
    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(GitHubName) ?? user.Identity?.Name ?? "";

    public static long? GetGitHubUserId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int? GetContributorId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ContributorId), out var id) ? id : null;
}
