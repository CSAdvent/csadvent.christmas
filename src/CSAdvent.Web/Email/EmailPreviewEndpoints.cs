using System.Net;
using System.Text;
using CSAdvent.Web.Content;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email;

/// <summary>
/// Development only: <c>/dev/emails</c> lists every email in <see cref="EmailCatalog"/> with when it's sent and who gets
/// it, and renders each one with sample data, as HTML or as its plain-text alternative.
/// </summary>
public static class EmailPreviewEndpoints
{
    public const string Path = "/dev/emails";

    public static WebApplication MapEmailPreviews(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        var previews = app.MapGroup(Path);
        previews.MapGet("/", () => Results.Content(Index(), "text/html; charset=utf-8"));
        previews.MapGet("/{key}", async (string key, EmailComposer composer, IOptions<SiteOptions> site) =>
            EmailCatalog.Find(key) is { } entry
                ? Results.Content(await composer.RenderHtmlAsync(entry.Sample(site.Value)), "text/html; charset=utf-8")
                : Results.NotFound());
        previews.MapGet("/{key}/text", (string key, EmailComposer composer, IOptions<SiteOptions> site) =>
            EmailCatalog.Find(key) is { } entry
                ? Results.Text(composer.RenderText(entry.Sample(site.Value)), "text/plain; charset=utf-8")
                : Results.NotFound());

        return app;
    }

    private static string Index()
    {
        var rows = new StringBuilder();
        foreach (var entry in EmailCatalog.Entries)
        {
            var key = WebUtility.HtmlEncode(entry.Key);
            rows.Append($"""
                <tr>
                  <td><strong>{WebUtility.HtmlEncode(entry.Name)}</strong></td>
                  <td>{WebUtility.HtmlEncode(entry.When)}</td>
                  <td>{WebUtility.HtmlEncode(entry.To)}</td>
                  <td><a href="{Path}/{key}">HTML</a> · <a href="{Path}/{key}/text">Text</a></td>
                </tr>
                """);
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>Emails · C# Advent</title>
              <style>
                body { font-family: system-ui, sans-serif; margin: 2rem; color: #334155; }
                h1 { color: #0f172a; }
                table { border-collapse: collapse; max-width: 72rem; }
                th, td { text-align: left; vertical-align: top; padding: 0.5rem 1rem 0.5rem 0; border-top: 1px solid #e2e8f0; }
                a { color: #164383; }
              </style>
            </head>
            <body>
              <h1>Emails the site sends</h1>
              <p>Development preview with sample data. Each email has a branded HTML version and a plain-text alternative.</p>
              <table>
                <tr><th>Email</th><th>When</th><th>To</th><th>Preview</th></tr>
                {{rows}}
              </table>
            </body>
            </html>
            """;
    }
}
