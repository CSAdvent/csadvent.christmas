using CSAdvent.Web.Content;

namespace CSAdvent.UnitTests.Content;

public sealed class MarkdownTests
{
    [Fact]
    public void RendersLinks()
    {
        var html = Markdown.ToHtml("[Originally hosted on crosscuttingconcerns.com](https://crosscuttingconcerns.com/x)").Value;

        Assert.Contains("<a href=\"https://crosscuttingconcerns.com/x\">Originally hosted on crosscuttingconcerns.com</a>", html);
    }

    [Fact]
    public void EscapesRawHtml()
    {
        var html = Markdown.ToHtml("Hello <script>alert(1)</script>").Value;

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("data:text/html,hi")]
    public void DropsLinksWithUnsafeSchemes(string url)
    {
        var html = Markdown.ToHtml($"[click](<{url}>)").Value;

        Assert.Contains("<a href=\"\">click</a>", html);
    }

    [Theory]
    [InlineData("https://csadvent.christmas/")]
    [InlineData("http://example.com/")]
    [InlineData("mailto:hello@example.com")]
    [InlineData("/archive/2017")]
    [InlineData("#december-6")]
    [InlineData("archive?year=2017")]
    public void KeepsSafeLinks(string url)
    {
        var html = Markdown.ToHtml($"[link]({url})").Value;

        Assert.Contains($"href=\"{url}\"", html);
    }
}
