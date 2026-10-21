namespace CSAdvent.Web.Auth;

public static class ReturnUrl
{
    /// <summary>
    /// The URL to come back to after signing in or out, if it's a path on this site. Anything else, including
    /// protocol-relative URLs like <c>//evil.example</c>, becomes the home page, so sign-in can't be used as an open redirect.
    /// </summary>
    public static string Sanitize(string? returnUrl) =>
        returnUrl is ['/', ..] && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
