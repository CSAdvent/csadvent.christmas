using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace CSAdvent.Web.Blurbs;

/// <summary>A link in a post's opening: its words, where it goes, and how far into the text it starts.</summary>
public sealed record OpeningLink(string Text, string Url, int Position);

/// <summary>
/// The opening of a post, as the models see it: its text from the top, with links written as <c>[words](url)</c>, and the
/// links themselves for the check that needs no model.
/// </summary>
public sealed record PostOpening(string Text, IReadOnlyList<OpeningLink> Links)
{
    /// <summary>A fingerprint of the text, so an unchanged post isn't sent to the models twice.</summary>
    public string Hash { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)));

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// What the page calls itself: its <c>title</c>, <c>og:title</c> and top headings, wherever they are on the page, so a post
    /// whose heading sits outside its article can still be recognized (#148).
    /// </summary>
    public IReadOnlyList<string> Titles { get; init; } = [];
}

/// <summary>
/// Reads the opening of an author's post from its page: the article itself, without the site's navigation, sidebars,
/// footers, scripts or styles, cut off after a set number of characters. A blurb in the post's footer isn't at the top, so it
/// isn't read.
/// </summary>
public static partial class ArticleReader
{
    /// <summary>Where a post's text usually is, best first. The whole body when none of them is there.</summary>
    private static readonly string[] Containers =
    [
        "main article",
        "article",
        "[itemprop=articleBody]",
        "#article-body",
        ".post-content",
        ".entry-content",
        ".article-content",
        "main",
        "[role=main]",
    ];

    /// <summary>Never part of the post's text.</summary>
    private const string Ignored =
        "script, style, noscript, template, svg, canvas, iframe, object, form, button, select, textarea, nav, aside, footer, " +
        "[role=navigation], [role=complementary], [role=contentinfo], [aria-hidden=true], [hidden]";

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "header", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "figure",
        "figcaption", "table", "tr", "td", "th", "dl", "dt", "dd", "hr", "br", "article", "main",
    };

    public static PostOpening Read(string html, Uri page, int maxCharacters)
    {
        var document = new HtmlParser().ParseDocument(html);
        var titles = TitlesOf(document);
        var root = Containers.Select(document.QuerySelector).FirstOrDefault(e => e is not null && HasText(e)) ?? document.Body;
        if (root is null)
        {
            return new PostOpening("", []) { Titles = titles };
        }

        foreach (var element in root.QuerySelectorAll(Ignored).ToList())
        {
            element.Remove();
        }

        // Without an article to go on, the site's own header is likely its navigation.
        if (root == document.Body)
        {
            foreach (var element in root.QuerySelectorAll("body > header, [role=banner]").ToList())
            {
                element.Remove();
            }
        }

        var writer = new OpeningWriter(page, maxCharacters);
        writer.Walk(root);
        return writer.Finish() with { Titles = titles };
    }

    private static bool HasText(IElement element) => !string.IsNullOrWhiteSpace(element.TextContent);

    /// <summary>The page's title, its Open Graph and Twitter titles, and its first few top headings.</summary>
    private static List<string> TitlesOf(AngleSharp.Html.Dom.IHtmlDocument document) =>
        [.. new[] { document.Title }
            .Concat(document.QuerySelectorAll("meta[property='og:title'], meta[name='twitter:title']").Select(meta => meta.GetAttribute("content")))
            .Concat(document.QuerySelectorAll("h1").Take(5).Select(heading => heading.TextContent))
            .Select(title => Collapse(title ?? "").Trim())
            .Where(title => title.Length > 0)
            .Distinct()];

    /// <summary>Collapses runs of whitespace into one space.</summary>
    public static string Collapse(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t]*\n[ \t\n]*")]
    private static partial Regex LineBreaks();

    /// <summary>Writes the text in document order until it has enough, counting only what readers see (not the link addresses).</summary>
    private sealed class OpeningWriter(Uri page, int maxCharacters)
    {
        private readonly StringBuilder _text = new();
        private readonly List<OpeningLink> _links = [];
        private int _visible;

        private bool IsFull => _visible >= maxCharacters;

        public void Walk(INode node)
        {
            if (IsFull)
            {
                return;
            }

            switch (node)
            {
                case IText text:
                    Append(Collapse(text.Data));
                    break;

                case IElement { LocalName: "a" } anchor when Link(anchor) is { } url:
                    var words = Collapse(anchor.TextContent).Trim();
                    if (words.Length == 0)
                    {
                        words = anchor.QuerySelector("img")?.GetAttribute("alt")?.Trim() is { Length: > 0 } alt ? alt : url;
                    }

                    if (_text.Length > 0 && !char.IsWhiteSpace(_text[^1]) && !char.IsPunctuation(_text[^1]))
                    {
                        _text.Append(' ');
                    }

                    _links.Add(new OpeningLink(words, url, _visible));
                    _text.Append('[').Append(words).Append("](").Append(url).Append(')');
                    _visible += words.Length;
                    break;

                case IElement element:
                    var block = Blocks.Contains(element.LocalName);
                    if (block)
                    {
                        Break();
                    }

                    foreach (var child in element.ChildNodes)
                    {
                        Walk(child);
                    }

                    if (block)
                    {
                        Break();
                    }

                    break;
            }
        }

        public PostOpening Finish()
        {
            var text = LineBreaks().Replace(_text.ToString(), "\n").Trim();
            return new PostOpening(text, _links);
        }

        private void Append(string text)
        {
            if (text.Length == 0 || (text == " " && (_text.Length == 0 || char.IsWhiteSpace(_text[^1]))))
            {
                return;
            }

            if (_text.Length > 0 && char.IsWhiteSpace(_text[^1]) && text[0] == ' ')
            {
                text = text[1..];
            }

            var room = maxCharacters - _visible;
            if (text.Length > room)
            {
                text = text[..room];
            }

            _text.Append(text);
            _visible += text.Length;
        }

        private void Break()
        {
            if (_text.Length > 0 && _text[^1] != '\n')
            {
                _text.Append('\n');
            }
        }

        /// <summary>The link's address, made absolute, if it's a web address.</summary>
        private string? Link(IElement anchor) =>
            anchor.GetAttribute("href") is { Length: > 0 } href
            && Uri.TryCreate(page, href.Trim(), out var url)
            && url.Scheme is "https" or "http"
                ? url.AbsoluteUri
                : null;
    }
}
