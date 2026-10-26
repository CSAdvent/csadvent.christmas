using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace CSAdvent.Web.Content;

/// <summary>
/// The security headers on every response (#165): HSTS, a Content-Security-Policy, a Permissions-Policy, framing only by
/// the site itself, and no <c>Server</c> header. They're set as each response starts, so redirects, error pages and static
/// files get them too, and the policy replaces any other (Blazor's interactive render mode sets a <c>frame-ancestors</c>-only one).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>
    /// A year, subdomains included, so a browser that has been here once only ever uses https. Hard to take back: browsers
    /// keep it for the whole year. Never <c>preload</c> without deciding that on purpose.
    /// </summary>
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// Features the site never uses, turned off. Only names browsers recognize (Chrome logs an error for any other), and not
    /// <c>clipboard-write</c>, which the organizers' Copy buttons use.
    /// </summary>
    public const string PermissionsPolicy =
        "accelerometer=(), browsing-topics=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    /// <summary>
    /// No <c>Server</c> header, and outside development, plain http (Container Apps passes it on, see <c>AppHost.cs</c>)
    /// goes to https in one permanent redirect.
    /// </summary>
    public static WebApplicationBuilder AddSecurityHeaders(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status301MovedPermanently;
                options.HttpsPort = 443;
            });
        }

        return builder;
    }

    /// <summary>Adds the headers to every response. Goes before anything that can answer by itself, such as the www redirect.</summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();

        // Worked out once, when the first response starts: by then every endpoint is mapped.
        var policies = new Lazy<Task<(string Site, string Organizers)>>(async () =>
        {
            var importMaps = await RenderImportMapsAsync(app);
            return (ContentSecurityPolicy(importMaps, isDevelopment), ContentSecurityPolicy(importMaps, isDevelopment, organizerPages: true));
        });

        app.Use((context, next) =>
        {
            context.Response.OnStarting(async () =>
            {
                var (site, organizers) = await policies.Value;
                Apply(context, context.Request.Path.StartsWithSegments(OrganizerPages) ? organizers : site, strictTransportSecurity: !isDevelopment);
            });
            return next(context);
        });
        return app;
    }

    /// <summary>The organizers' pages, which show GitHub's avatars (the author pages).</summary>
    public const string OrganizerPages = "/manage";

    /// <summary>
    /// Sets the headers on a response. HSTS only over https (a browser ignores it over http), and never in development, where
    /// it would stick to localhost.
    /// </summary>
    public static void Apply(HttpContext context, string contentSecurityPolicy, bool strictTransportSecurity)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        // frame-ancestors 'self', for browsers that only know the older header.
        headers.XFrameOptions = "SAMEORIGIN";
        // Links out to authors' posts still tell them the visit came from here.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers.ContentSecurityPolicy = contentSecurityPolicy;
        if (strictTransportSecurity && context.Request.IsHttps)
        {
            headers.StrictTransportSecurity = StrictTransportSecurity;
        }
    }

    /// <summary>
    /// The Content-Security-Policy. Scripts only come from this site, plus the inline scripts given here by their SHA-256
    /// hash: Blazor's import map, as <c>&lt;ImportMap /&gt;</c> would render it. <c>App.razor</c> has loaded no Blazor
    /// script since #185, but the policy stays right if it ever does again. A browser hashes a script's text after HTML's
    /// newline normalization (CR LF and CR become LF), so the hash is too: a Windows build writes its import map with CR LF.
    /// Any other inline script is refused (JSON-LD isn't a script that runs, so it's fine).
    /// <list type="bullet">
    /// <item>Styles may be inline: the retrospectives' charts size their bars with <c>style</c>, and emails are all inline styles.</item>
    /// <item>Images only come from the site, so visitors' browsers never ask anyone else (#168 serves the About page's photos
    /// itself). The organizers' pages can also show GitHub's avatars (<c>github.com/{login}.png</c> redirects to
    /// <c>avatars.githubusercontent.com</c>).</item>
    /// <item>Connections can go to any https site. Nothing the site loads connects anywhere now, but Blazor's enhanced
    /// navigation (wherever <c>blazor.web.js</c> is loaded) fetches same-site links, and two of them redirect elsewhere
    /// (<c>/go</c> to the post, and <c>/signin</c> to GitHub): if the policy blocked the redirect, the click would do
    /// nothing. In development, the browser refresh script's WebSocket can reach localhost too.</item>
    /// <item>Forms can post to GitHub, because a post with an expired sign-in is sent to sign in again.</item>
    /// </list>
    /// </summary>
    public static string ContentSecurityPolicy(IEnumerable<string> inlineScripts, bool isDevelopment, bool organizerPages = false)
    {
        var hashes = inlineScripts
            .Select(script => script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
            .Select(script => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'")
            .Distinct()
            .Order(StringComparer.Ordinal);

        return string.Join("; ",
            "default-src 'self'",
            string.Join(' ', ["script-src 'self'", .. hashes]),
            "style-src 'self' 'unsafe-inline'",
            organizerPages ? "img-src 'self' https://github.com https://avatars.githubusercontent.com" : "img-src 'self'",
            isDevelopment ? "connect-src 'self' https: ws://localhost:* wss://localhost:*" : "connect-src 'self' https:",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self' https://github.com",
            "frame-ancestors 'self'");
    }

    /// <summary>
    /// The import maps the site's pages render, rendered here the same way: <c>&lt;ImportMap /&gt;</c> with the import map
    /// on the page's endpoint (or one made from its static assets).
    /// </summary>
    private static async Task<List<string>> RenderImportMapsAsync(WebApplication app)
    {
        var definitions = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ImportMapDefinition>()
                ?? (endpoint.Metadata.GetMetadata<ResourceAssetCollection>() is { } assets ? ImportMapDefinition.FromResourceCollection(assets) : null))
            .OfType<ImportMapDefinition>()
            .DistinctBy(definition => definition.ToString())
            .ToList();

        await using var scope = app.Services.CreateAsyncScope();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        var scripts = new List<string>();
        foreach (var definition in definitions)
        {
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ImportMap.ImportMapDefinition)] = definition });
                return (await renderer.RenderComponentAsync<ImportMap>(parameters)).ToHtmlString();
            });

            // The script's text, between its tags.
            var start = html.IndexOf('>', StringComparison.Ordinal) + 1;
            var end = html.LastIndexOf("</script>", StringComparison.Ordinal);
            if (start > 0 && end >= start)
            {
                scripts.Add(html[start..end]);
            }
        }

        return scripts;
    }
}
