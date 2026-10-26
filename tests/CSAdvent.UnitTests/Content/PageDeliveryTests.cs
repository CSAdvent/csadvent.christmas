using System.Security.Claims;
using System.Text;
using CSAdvent.Web.Content;
using Microsoft.AspNetCore.Http;

namespace CSAdvent.UnitTests.Content;

/// <summary>How pages are cached (#186) and which are compressed (#164).</summary>
public sealed class PageDeliveryTests
{
    [Fact]
    public void APageKeepsItsETagUntilItChanges()
    {
        var etag = PageCaching.ETag(Encoding.UTF8.GetBytes("<p>Day 6</p>"));

        // Weak, because the same page is sent compressed or not.
        Assert.True(etag.IsWeak);
        Assert.Equal(etag, PageCaching.ETag(Encoding.UTF8.GetBytes("<p>Day 6</p>")));
        Assert.NotEqual(etag, PageCaching.ETag(Encoding.UTF8.GetBytes("<p>Day 7</p>")));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("W/\"0123456789abcdef\"", true)]
    [InlineData("\"0123456789abcdef\"", true)]
    [InlineData("W/\"fedcba9876543210\", W/\"0123456789abcdef\"", true)]
    [InlineData("*", true)]
    [InlineData("W/\"fedcba9876543210\"", false)]
    public void AnUnchangedPageIsNotSentAgain(string? ifNoneMatch, bool unchanged)
    {
        var context = new DefaultHttpContext();
        if (ifNoneMatch is not null)
        {
            context.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\"0123456789abcdef\"", isWeak: true);

        Assert.Equal(unchanged, PageCaching.IsUnchanged(context.Request, etag));
    }

    [Theory]
    [InlineData(false, "text/html; charset=utf-8", false)]
    [InlineData(true, "text/html; charset=utf-8", true)]
    [InlineData(true, "application/rss+xml; charset=utf-8", false)]
    public void OnlyASignedInVisitorsPagesAreLeftUncompressed(bool signedIn, string contentType, bool personal)
    {
        var context = new DefaultHttpContext();
        if (signedIn)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ada")], "test"));
        }

        context.Response.ContentType = contentType;

        Assert.Equal(personal, PageCompressionProvider.IsPersonal(context));
    }
}
