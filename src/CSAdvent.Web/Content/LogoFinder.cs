using System.Globalization;
using System.Text.RegularExpressions;

namespace CSAdvent.Web.Content;

/// <summary>Where a site's logo might be, best first, from the icons its home page declares and the usual fallbacks.</summary>
public static partial class LogoFinder
{
    /// <summary>
    /// Apple touch icons first (usually 180 pixels), then other icons, biggest first, then <c>/apple-touch-icon.png</c> and
    /// <c>/favicon.ico</c>. SVG icons are skipped, because they can't be redrawn as PNGs.
    /// </summary>
    public static IReadOnlyList<Uri> FindCandidates(string html, Uri page)
    {
        var baseUri = BaseTag().Match(html) is { Success: true } baseTag && Uri.TryCreate(page, Attribute(baseTag.Value, "href"), out var declared)
            ? declared
            : page;

        var icons = new List<(Uri Uri, int Rank, int Size)>();
        foreach (Match link in LinkTag().Matches(html))
        {
            var rel = Attribute(link.Value, "rel")?.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var rank = rel.Any(r => r.StartsWith("apple-touch-icon", StringComparison.Ordinal)) ? 0 : rel.Contains("icon") ? 1 : -1;
            var href = Attribute(link.Value, "href");
            if (rank < 0 || string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(baseUri, href.Trim(), out var uri) || !IsWeb(uri))
            {
                continue;
            }

            var type = Attribute(link.Value, "type");
            if (string.Equals(type, "image/svg+xml", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            icons.Add((uri, rank, LargestSize(Attribute(link.Value, "sizes"))));
        }

        var origin = new Uri(page.GetLeftPart(UriPartial.Authority));
        return
        [
            .. icons.OrderBy(i => i.Rank).ThenByDescending(i => i.Size).Select(i => i.Uri)
                .Append(new Uri(origin, "/apple-touch-icon.png"))
                .Append(new Uri(origin, "/favicon.ico"))
                .Distinct(),
        ];
    }

    private static bool IsWeb(Uri uri) => uri.Scheme is "https" or "http";

    /// <summary>The longest side in a <c>sizes</c> attribute ("16x16 32x32" is 32). Unknown sizes count as 32, a typical favicon.</summary>
    private static int LargestSize(string? sizes)
    {
        var largest = 0;
        foreach (var size in (sizes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = size.Split('x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
            {
                largest = Math.Max(largest, Math.Max(width, height));
            }
        }

        return largest > 0 ? largest : 32;
    }

    private static string? Attribute(string tag, string name)
    {
        foreach (Match attribute in TagAttribute().Matches(tag))
        {
            if (string.Equals(attribute.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return System.Net.WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            }
        }

        return null;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"<base\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseTag();

    [GeneratedRegex(@"(?<name>[a-zA-Z][\w-]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'>]+))")]
    private static partial Regex TagAttribute();
}
