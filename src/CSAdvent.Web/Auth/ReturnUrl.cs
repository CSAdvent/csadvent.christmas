namespace CSAdvent.Web.Auth;

public static class ReturnUrl
{
    /// <summary>
    /// The URL to come back to after signing in or out, if it's a path on this site. Anything else, including
    /// protocol-relative URLs like <c>//evil.example</c>, becomes the home page, so sign-in can't be used as an open redirect.
    /// </summary>
    /// <remarks>
    /// These are ASP.NET Core's <c>IsLocalUrl</c> rules, control characters included: browsers drop tabs and line breaks
    /// from URLs, so <c>/&lt;tab&gt;/evil.example</c> would become <c>//evil.example</c>. GitHub sign-in redirects to this
    /// URL without checking it again.
    /// </remarks>
    public static string Sanitize(string? returnUrl) =>
        returnUrl is ['/', ..]
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            && !returnUrl.Any(char.IsControl)
            ? returnUrl
            : "/";

    /// <summary>
    /// Adds <c>name=value</c> to a URL's query string, before its fragment if it has one, such as the <c>#id</c> of the item
    /// an organizer's button was on, so the page comes back to it (#170).
    /// </summary>
    public static string WithQuery(string url, string name, string value)
    {
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        var (path, fragment) = hash < 0 ? (url, "") : (url[..hash], url[hash..]);
        return $"{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{name}={value}{fragment}";
    }
}
