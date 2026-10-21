using System.Security.Claims;

namespace CSAdvent.Web.Auth;

/// <summary>
/// The claims in a signed-in user's cookie: the GitHub user ID (<see cref="ClaimTypes.NameIdentifier"/>), the GitHub login
/// (<see cref="ClaimTypes.Name"/>), the verified email if there is one, and the contributor's ID in our database.
/// </summary>
public static class AdventClaims
{
    public const string ContributorId = "csadvent:contributor_id";

    public static IEnumerable<Claim> For(long githubUserId, string login, string? verifiedEmail, int contributorId)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, githubUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new Claim(ClaimTypes.Name, login);
        yield return new Claim(ContributorId, contributorId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (verifiedEmail is not null)
        {
            yield return new Claim(ClaimTypes.Email, verifiedEmail);
        }
    }

    public static long? GetGitHubUserId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int? GetContributorId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ContributorId), out var id) ? id : null;
}
