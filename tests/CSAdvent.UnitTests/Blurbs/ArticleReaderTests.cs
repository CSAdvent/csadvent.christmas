using CSAdvent.Web.Blurbs;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class ArticleReaderTests
{
    private static readonly Uri Page = new("https://blog.example.com/posts/span");

    /// <summary>An ordinary post about as long as a fetched page can be (2 MB), which takes a moment to read.</summary>
    internal static readonly string LongPage =
        $"<article>{string.Concat(Enumerable.Repeat("<p>Span&lt;T&gt; lets you work with contiguous memory without copying it.</p>", 26_000))}</article>";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsTheArticleWithItsLinksAndWithoutTheSiteAroundIt()
    {
        var opening = await ReadAsync(
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
    public async Task ABlurbInThePostsFooterIsntAtTheTop()
    {
        var opening = await ReadAsync(
            """<article><h1>Title</h1><p>Text.</p><footer>Part of the <a href="https://csadvent.christmas">2026 C# Advent calendar</a>.</footer></article>""",
            Page,
            2000);

        Assert.Empty(opening.Links);
        Assert.DoesNotContain("Advent", opening.Text);
    }

    [Fact]
    public async Task StopsAfterTheOpening()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 1000));
        var opening = await ReadAsync($"""<article><p>{words}</p><p><a href="https://csadvent.christmas">C# Advent</a></p></article>""", Page, 500);

        Assert.InRange(opening.Text.Length, 495, 500);
        Assert.Empty(opening.Links);
    }

    [Fact]
    public async Task WithoutAnArticleItReadsThePageWithoutItsHeader()
    {
        var opening = await ReadAsync(
            """<body><header><a href="https://csadvent.christmas">Site menu</a></header><div><p>Post text, part of C# Advent.</p></div></body>""",
            Page,
            2000);

        Assert.Equal("Post text, part of C# Advent.", opening.Text);
        Assert.Empty(opening.Links);
    }

    [Fact]
    public async Task AnImageLinkIsNamedByItsAltText()
    {
        var opening = await ReadAsync("""<article><a href="https://csadvent.christmas"><img src="badge.png" alt="C# Advent 2026"></a></article>""", Page, 2000);

        Assert.Equal("[C# Advent 2026](https://csadvent.christmas/)", opening.Text);
    }

    [Fact]
    public async Task TheHashChangesOnlyWithTheText()
    {
        var first = await ReadAsync("<article><p>One</p></article>", Page, 2000);
        var same = await ReadAsync("<html><body><article><p>One</p></article><script>x()</script></body></html>", Page, 2000);
        var changed = await ReadAsync("<article><p>Two</p></article>", Page, 2000);

        Assert.Equal(first.Hash, same.Hash);
        Assert.NotEqual(first.Hash, changed.Hash);
    }

    /// <summary>
    /// The reader (and AngleSharp's <c>TextContent</c> before 1.9) walks the page recursively, so a page nested deeper than the
    /// stack allows would crash the whole site, not just its blurb check. AngleSharp stops nesting at
    /// <c>HtmlParserOptions.MaximumDepth</c> (since 1.8.6), which keeps every walk shallow. Spans keep the test quick.
    /// </summary>
    [Fact]
    public async Task APageNestedDeeperThanTheStackAllowsIsStillRead()
    {
        const int depth = 100_000;
        var html = $"""<article><p>Part of the <a href="https://csadvent.christmas">2026 C# Advent calendar</a>.</p>{string.Concat(Enumerable.Repeat("<span>", depth))}Deep{string.Concat(Enumerable.Repeat("</span>", depth))}</article>""";

        var opening = await ReadAsync(html, Page, 2000);

        Assert.StartsWith("Part of the [2026 C# Advent calendar](https://csadvent.christmas/).", opening.Text);
        Assert.Equal("https://csadvent.christmas/", Assert.Single(opening.Links).Url);
    }

    [Fact]
    public async Task KeepsWhatThePageCallsItselfWhereverItIs()
    {
        var opening = await ReadAsync(
            """
            <html><head><title>Span&lt;T&gt; · My blog</title><meta property="og:title" content="Span&lt;T&gt; all the way down"></head>
            <body>
              <header><h1>Span&lt;T&gt;   all the way down</h1></header>
              <article><p>Let's slice some memory.</p></article>
            </body></html>
            """,
            Page,
            2000);

        Assert.Equal("Let's slice some memory.", opening.Text);
        Assert.Equal(["Span<T> · My blog", "Span<T> all the way down"], opening.Titles);
    }

    [Fact]
    public async Task APageThatTakesLongerThanTheLimitToReadIsGivenUpOn()
    {
        var opening = await ArticleReader.ReadAsync(LongPage, Page, 2000, TimeSpan.FromMilliseconds(1), CancellationToken);

        Assert.Null(opening);
    }

    [Fact]
    public async Task ALongPageIsReadWithinTheLimit()
    {
        var opening = await ReadAsync(LongPage, Page, 500);

        Assert.StartsWith("Span<T> lets you work with contiguous memory without copying it.", opening.Text);
    }

    [Fact]
    public async Task TheCallersOwnCancellationIsntMistakenForTheLimit()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArticleReader.ReadAsync(LongPage, Page, 2000, TimeSpan.FromMinutes(1), canceled.Token));
    }

    /// <summary>Reads a page with a limit no test page comes near.</summary>
    private static async Task<PostOpening> ReadAsync(string html, Uri page, int maxCharacters)
    {
        var opening = await ArticleReader.ReadAsync(html, page, maxCharacters, TimeSpan.FromMinutes(1), CancellationToken);
        Assert.NotNull(opening);
        return opening;
    }
}
