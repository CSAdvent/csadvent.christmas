using System.Security.Cryptography;
using System.Text;
using CSAdvent.Web.Content;
using Microsoft.AspNetCore.Http;

namespace CSAdvent.UnitTests.Content;

public sealed class SecurityHeadersTests
{
    private const string ImportMap = """{"imports":{"./js/copy.js":"./js/copy.abc123.js"}}""";

    [Fact]
    public void ThePolicyOnlyRunsScriptsFromTheSiteAndTheInlineScriptsItWasGiven()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy([ImportMap, ImportMap], isDevelopment: false);
        var directives = policy.Split("; ").ToDictionary(d => d.Split(' ')[0]);

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(ImportMap)));
        Assert.Equal($"script-src 'self' 'sha256-{hash}'", directives["script-src"]);
        Assert.Equal("default-src 'self'", directives["default-src"]);
        Assert.Equal("object-src 'none'", directives["object-src"]);
        Assert.Equal("base-uri 'self'", directives["base-uri"]);
        Assert.Equal("form-action 'self' https://github.com", directives["form-action"]);
        Assert.Equal("frame-ancestors 'self'", directives["frame-ancestors"]);
        // Visitors' browsers never fetch images from anyone else.
        Assert.Equal("img-src 'self'", directives["img-src"]);
        Assert.Equal("connect-src 'self' https:", directives["connect-src"]);
        Assert.DoesNotContain("unsafe-eval", policy);
    }

    [Fact]
    public void ScriptsAreHashedAsBrowsersReadThemWithLineFeeds()
    {
        // A Windows build writes the import map with CR LF; browsers hash it after turning those into LF.
        var windows = SecurityHeaders.ContentSecurityPolicy(["{\r\n  \"imports\": {}\r\n}"], isDevelopment: false);
        var linux = SecurityHeaders.ContentSecurityPolicy(["{\n  \"imports\": {}\n}"], isDevelopment: false);

        Assert.Equal(linux, windows);
    }

    [Fact]
    public void OrganizerPagesCanShowGitHubsAvatars()
    {
        var site = SecurityHeaders.ContentSecurityPolicy([ImportMap], isDevelopment: false);
        var organizers = SecurityHeaders.ContentSecurityPolicy([ImportMap], isDevelopment: false, organizerPages: true);

        Assert.Contains("img-src 'self' https://github.com https://avatars.githubusercontent.com;", organizers);
        Assert.Equal(site, organizers.Replace(" https://github.com https://avatars.githubusercontent.com", "", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutInlineScriptsOnlyTheSitesOwnScriptsRun()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy([], isDevelopment: false);

        Assert.Contains("script-src 'self';", policy);
    }

    [Fact]
    public void DevelopmentLetsTheBrowserRefreshScriptConnectToLocalhost()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy([], isDevelopment: true);

        Assert.Contains("connect-src 'self' https: ws://localhost:* wss://localhost:*", policy);
    }

    [Fact]
    public void EveryResponseGetsTheHeadersAndHttpsOnesGetHsts()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        // Blazor's own policy only has frame-ancestors; ours replaces it.
        context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";

        SecurityHeaders.Apply(context, "default-src 'self'", strictTransportSecurity: true);

        var headers = context.Response.Headers;
        Assert.Equal("max-age=31536000; includeSubDomains", headers.StrictTransportSecurity.ToString());
        Assert.Equal("default-src 'self'", headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("SAMEORIGIN", headers.XFrameOptions.ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Equal(SecurityHeaders.PermissionsPolicy, headers["Permissions-Policy"].ToString());
        Assert.Contains("camera=()", SecurityHeaders.PermissionsPolicy);
        // The organizers' Copy buttons write to the clipboard.
        Assert.DoesNotContain("clipboard", SecurityHeaders.PermissionsPolicy);
    }

    [Theory]
    [InlineData("http", true)]
    [InlineData("https", false)]
    public void HstsIsOnlySentOverHttpsOutsideDevelopment(string scheme, bool strictTransportSecurity)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;

        SecurityHeaders.Apply(context, "default-src 'self'", strictTransportSecurity);

        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.Equal("default-src 'self'", context.Response.Headers.ContentSecurityPolicy.ToString());
    }
}
