using Bunit;
using CSAdvent.Web.Components.Entries;
using CSAdvent.Web.DeadLinks;
using CSAdvent.Web.Entries;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class GonePostTests : BunitContext
{
    private const string Post = "https://blog.example.com/2018/12/05/span-of-t/";
    private const string Copy = "https://web.archive.org/web/20181205101112/https://blog.example.com/2018/12/05/span-of-t/";

    [Fact]
    public void ReadersOnlyGetTheCopyOfAPostThatsGone()
    {
        Assert.Equal(Post, new PostDestination(1, Post).Url);
        Assert.Equal(Post, new PostDestination(1, Post, IsGone: false, ArchivedUrl: Copy).Url);
        Assert.Equal(Post, new PostDestination(1, Post, IsGone: true).Url);
        Assert.Equal(Copy, new PostDestination(1, Post, IsGone: true, ArchivedUrl: Copy).Url);
        Assert.True(new PostDestination(1, Post, IsGone: true, ArchivedUrl: Copy).IsArchivedCopy);
    }

    [Fact]
    public void AGonePostsPageSaysItsTheSavedCopy()
    {
        var notice = Render<GonePostNotice>(p => p.Add(c => c.Destination, new PostDestination(1, Post, IsGone: true, ArchivedUrl: Copy)));

        Assert.Contains("This post seems to have gone from its site, so the button below takes you to the copy the Internet Archive's Wayback Machine saved instead.", Normalize(notice.Markup));
        var original = notice.Find("a");
        Assert.Equal(Post, original.GetAttribute("href"));
        Assert.Equal("nofollow", original.GetAttribute("rel"));
        Assert.Equal("Read the saved copy", GonePostNotice.ButtonLabel(new PostDestination(1, Post, IsGone: true, ArchivedUrl: Copy)));
    }

    [Fact]
    public void WithoutACopyItSaysSo()
    {
        var destination = new PostDestination(1, Post, IsGone: true);
        var notice = Render<GonePostNotice>(p => p.Add(c => c.Destination, destination));

        Assert.Contains("we haven't found a saved copy of it yet", notice.Markup);
        Assert.Empty(notice.FindAll("a"));
        Assert.Equal("Read the post", GonePostNotice.ButtonLabel(destination));
    }

    [Fact]
    public void APostThatsThereSaysNothing()
    {
        Assert.Equal("", Render<GonePostNotice>(p => p.Add(c => c.Destination, new PostDestination(1, Post))).Markup.Trim());
        Assert.Equal("Read the post", GonePostNotice.ButtonLabel(null));
    }

    [Theory]
    [InlineData("checked-42", 42, "checked")]
    [InlineData("invalid-42", 42, "invalid")]
    [InlineData("checked-42", 7, null)]
    [InlineData("checked", 42, null)]
    [InlineData(null, 42, null)]
    public void AButtonsResultIsForItsOwnPost(string? query, int slotId, string? done) =>
        Assert.Equal(done, LinkCheckDone.For(query, slotId));

    private static string Normalize(string markup) => System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(markup), @"\s+", " ");
}
