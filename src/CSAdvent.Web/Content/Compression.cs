using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Content;

/// <summary>
/// Compresses what the app writes (#164): pages, the feeds, the sitemap and calendar files, with Brotli or gzip. Static files
/// are compressed when the app is built, and MapStaticAssets serves them that way.
/// </summary>
public static class Compression
{
    public static IServiceCollection AddCompression(this IServiceCollection services)
    {
        // Registered first, so AddResponseCompression keeps it.
        services.AddSingleton<IResponseCompressionProvider, PageCompressionProvider>();
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
        return services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = [.. ResponseCompressionDefaults.MimeTypes, "application/rss+xml", "text/calendar"];
        });
    }
}

/// <summary>
/// Leaves out the pages a BREACH attack could learn something from: a signed-in visitor's, which carry their details and
/// their sign-out form's antiforgery token. A page for someone who isn't signed in has nothing to learn, even with a form:
/// its antiforgery token goes with a SameSite=Strict cookie, which a request from another site doesn't send, so that request
/// gets a new token rather than the visitor's.
/// </summary>
public sealed class PageCompressionProvider(IServiceProvider services, IOptions<ResponseCompressionOptions> options)
    : ResponseCompressionProvider(services, options)
{
    public override bool ShouldCompressResponse(HttpContext context) => base.ShouldCompressResponse(context) && !IsPersonal(context);

    public static bool IsPersonal(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
