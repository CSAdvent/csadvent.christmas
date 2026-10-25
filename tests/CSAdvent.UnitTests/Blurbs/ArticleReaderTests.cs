using CSAdvent.Web.Blurbs;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class ArticleReaderTests
{
    private static readonly Uri Page = new("https://blog.example.com/posts/span");

    [Fact]
    public void ReadsTheArticleWithItsLinksAndWithoutTheSiteAroundIt()
    {
        var opening = ArticleReader.Read(
            """
            <html><head><title>Span</title><style>.x { color: red }</style></head>
            <body>
              <header><nav><a href="/">Home</a> <a href="/about">About</a></nav></header>
              <main><article>
                <h1>Span&lt;T&gt; all the way down</h1>
                <p>This post is part of the <a href="https://csadvent.christmas">2026 C# Advent calendar</a>.</p>
                <script>var tracking = "csadvent.christmas";</script>
                <aside>Subscribe to my newsletter!</aside>
                <p>Let's <a href="/tags/span">slice</a> some memory.</p>
              </article></main>
              <footer><p>© My blog</p></footer>
            </body></html>
            """,
            Page,
            maxCharacters: 2000);

        Assert.Equal(
            "Span<T> all the way down\nThis post is part of the [2026 C# Advent calendar](https://csadvent.christmas/).\nLet's [slice](https://blog.example.com/tags/span) some memory.",
            opening.Text);
        Assert.Equal(
            [("2026 C# Advent calendar", "https://csadvent.christmas/"), ("slice", "https://blog.example.com/tags/span")],
            opening.Links.Select(l => (l.Text, l.Url)));
        Assert.Equal(49, opening.Links[0].Position);
    }

    [Fact]
    public void ABlurbInThePostsFooterIsntAtTheTop()
    {
        var opening = ArticleReader.Read(
            """<article><h1>Title</h1><p>Text.</p><footer>Part of the <a href="https://csadvent.christmas">2026 C# Advent calendar</a>.</footer></article>""",
            Page,
            2000);

        Assert.Empty(opening.Links);
        Assert.DoesNotContain("Advent", opening.Text);
    }

    [Fact]
    public void StopsAfterTheOpening()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 1000));
        var opening = ArticleReader.Read($"""<article><p>{words}</p><p><a href="https://csadvent.christmas">C# Advent</a></p></article>""", Page, 500);

        Assert.InRange(opening.Text.Length, 495, 500);
        Assert.Empty(opening.Links);
    }

    [Fact]
    public void WithoutAnArticleItReadsThePageWithoutItsHeader()
    {
        var opening = ArticleReader.Read(
            """<body><header><a href="https://csadvent.christmas">Site menu</a></header><div><p>Post text, part of C# Advent.</p></div></body>""",
            Page,
            2000);

        Assert.Equal("Post text, part of C# Advent.", opening.Text);
        Assert.Empty(opening.Links);
    }

    [Fact]
    public void AnImageLinkIsNamedByItsAltText()
    {
        var opening = ArticleReader.Read("""<article><a href="https://csadvent.christmas"><img src="badge.png" alt="C# Advent 2026"></a></article>""", Page, 2000);

        Assert.Equal("[C# Advent 2026](https://csadvent.christmas/)", opening.Text);
    }

    [Fact]
    public void TheHashChangesOnlyWithTheText()
    {
        var first = ArticleReader.Read("<article><p>One</p></article>", Page, 2000);
        var same = ArticleReader.Read("<html><body><article><p>One</p></article><script>x()</script></body></html>", Page, 2000);
        var changed = ArticleReader.Read("<article><p>Two</p></article>", Page, 2000);

        Assert.Equal(first.Hash, same.Hash);
        Assert.NotEqual(first.Hash, changed.Hash);
    }
}
