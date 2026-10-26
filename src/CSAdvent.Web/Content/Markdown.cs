using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace CSAdvent.Web.Content;

/// <summary>Renders the organizer's Markdown (season notes, and later the editable content blocks) to HTML.</summary>
public static class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    /// <summary>
    /// Raw HTML is escaped instead of rendered, and links that aren't http(s), mailto or relative (such as
    /// <c>javascript:</c>) are dropped, so stored copy can't inject markup or script.
    /// </summary>
    public static MarkupString ToHtml(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url))
            {
                link.Url = "";
            }
        }

        // Autolinks (<https://…>) are links too, and their text is their address, so an unsafe one becomes plain text (#191).
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!IsSafeUrl(autolink.Url))
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        return new MarkupString(writer.ToString());
    }

    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        // A colon before the first '/', '?' or '#' starts a scheme; anything else is a relative link.
        var colon = url.IndexOf(':');
        var delimiter = url.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (delimiter >= 0 && delimiter < colon))
        {
            return true;
        }

        return url[..colon].ToLowerInvariant() is "http" or "https" or "mailto";
    }
}
