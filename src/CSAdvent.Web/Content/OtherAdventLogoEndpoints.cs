using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Content;

/// <summary>What the Refresh and Upload buttons on the Other Advents admin page post. Plain forms, because there's one per calendar.</summary>
public sealed class OtherAdventLogoForm
{
    public int Year { get; set; }

    public int Id { get; set; }

    /// <summary>The uploaded image. Not used by Refresh.</summary>
    public IFormFile? Logo { get; set; }
}

public static class OtherAdventLogoEndpoints
{
    /// <summary>How long a Refresh waits for the site before giving up.</summary>
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    public static WebApplication MapOtherAdventLogos(this WebApplication app)
    {
        // Every logo is a PNG we drew ourselves (LogoImages), and its link changes with it, so it can be cached for a long time.
        app.MapGet("/others/logos/{id:int}.png", async (int id, OtherAdventLogoService logos, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await logos.GetPngAsync(id, cancellationToken) is not { } png)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "public, max-age=604800";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(png, "image/png");
        });

        // Binding a form makes these check the antiforgery token.
        app.MapPost("/manage/other-advents/logo/refresh", async ([FromForm] OtherAdventLogoForm form, OtherAdventsService others, OtherAdventLogoService logos, CancellationToken cancellationToken) =>
            {
                if (await others.FindUrlAsync(form.Year, form.Id, cancellationToken) is not { } url)
                {
                    return Results.NotFound();
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RefreshTimeout);
                LogoResult result;
                try
                {
                    result = await logos.FetchAsync(url, timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = LogoResult.NotFound;
                }

                return Back(form.Year, result == LogoResult.Saved ? "refreshed" : "not-found");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        app.MapPost("/manage/other-advents/logo/upload", async ([FromForm] OtherAdventLogoForm form, OtherAdventsService others, OtherAdventLogoService logos, CancellationToken cancellationToken) =>
            {
                if (await others.FindUrlAsync(form.Year, form.Id, cancellationToken) is not { } url)
                {
                    return Results.NotFound();
                }

                if (form.Logo is not { Length: > 0 and <= OtherAdventLogoService.MaxUploadBytes } file)
                {
                    return Back(form.Year, "invalid");
                }

                using var image = new MemoryStream();
                await file.CopyToAsync(image, cancellationToken);
                var result = await logos.UploadAsync(url, image.ToArray(), cancellationToken);
                return Back(form.Year, result == LogoResult.Saved ? "uploaded" : "invalid");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }

    private static IResult Back(int year, string logo) => Results.LocalRedirect($"/manage/other-advents?year={year}&logo={logo}");
}
