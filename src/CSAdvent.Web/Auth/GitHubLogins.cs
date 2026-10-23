using System.Text.RegularExpressions;

namespace CSAdvent.Web.Auth;

/// <summary>GitHub logins as people type them: "octocat", "@octocat", or a GitHub profile URL.</summary>
public static partial class GitHubLogins
{
    /// <summary>A GitHub login, lowercase, from "login", "@login" or a GitHub profile URL; null if it can't be one.</summary>
    public static string? Normalize(string? input)
    {
        var login = (input ?? "").Trim();
        if (Uri.TryCreate(login, UriKind.Absolute, out var uri) && uri.Host is "github.com" or "www.github.com")
        {
            login = uri.AbsolutePath.Trim('/');
        }

        login = login.TrimStart('@');
        return GitHubLogin().IsMatch(login) ? login.ToLowerInvariant() : null;
    }

    /// <summary>Letters, digits and single hyphens between them, up to 39 characters.</summary>
    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$")]
    private static partial Regex GitHubLogin();
}
