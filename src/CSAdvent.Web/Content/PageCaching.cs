using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.Net.Http.Headers;

namespace CSAdvent.Web.Content;

/// <summary>
/// Lets browsers keep pages (#186). A page that doesn't set its own Cache-Control is <c>private, no-cache</c>: the browser
/// may keep it (for going back, and its back/forward cache), but asks again before showing it otherwise, and an unchanged
/// page is answered with a 304 thanks to its ETag. Pages with a form keep the <c>no-store</c> that antiforgery gives them.
/// </summary>
public static class PageCaching
{
    public const string CacheControl = "private, no-cache";

    /// <summary>Goes inside response compression, so the ETag is the page's own, whatever its encoding.</summary>
    public static WebApplication UsePageCaching(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // Only pages, and only GETs: a form post always renders afresh.
            if (!HttpMethods.IsGet(context.Request.Method) || context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is null)
            {
                await next();
                return;
            }

            // The page is rendered into memory first (it isn't streamed anyway), so its ETag can be worked out before it's sent.
            var body = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next();
            }
            finally
            {
                context.Response.Body = body;
            }

            var response = context.Response;
            if (response.Headers.CacheControl.Count == 0 && response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                response.Headers.CacheControl = CacheControl;
                if (response.StatusCode == StatusCodes.Status200OK)
                {
                    var etag = ETag(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                    response.Headers.ETag = etag.ToString();
                    if (IsUnchanged(context.Request, etag))
                    {
                        response.StatusCode = StatusCodes.Status304NotModified;
                        return;
                    }
                }
            }

            if (buffer.Length > 0)
            {
                response.ContentLength = buffer.Length;
                await body.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), context.RequestAborted);
            }
        });
        return app;
    }

    /// <summary>A weak ETag (the same page compressed or not) from a hash of the HTML.</summary>
    public static EntityTagHeaderValue ETag(ReadOnlySpan<byte> html) =>
        new($"\"{Convert.ToHexStringLower(SHA256.HashData(html))[..16]}\"", isWeak: true);

    /// <summary>Whether the browser already has this version of the page (If-None-Match, compared weakly).</summary>
    public static bool IsUnchanged(HttpRequest request, EntityTagHeaderValue etag) =>
        request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } tags
        && tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false));
}
