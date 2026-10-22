using CSAdvent.Web.Content;

namespace CSAdvent.UnitTests.Content;

public sealed class ContentCatalogTests
{
    [Fact]
    public void EveryBlockHasDefaultCopyThatRenders()
    {
        foreach (var definition in ContentCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Default), definition.Key);
            Assert.NotEmpty(Markdown.ToHtml(definition.Default).Value);
            Assert.Same(definition, ContentCatalog.Find(definition.Key));
        }

        Assert.Null(ContentCatalog.Find("nope"));
    }

    [Fact]
    public void TheAboutCopyCarriesTheHistoryAndRules()
    {
        var about = Markdown.ToHtml(ContentCatalog.Find(ContentCatalog.About)!.Default).Value;

        Assert.Contains("<h2>History</h2>", about);
        Assert.Contains("<ol>", about);
        Assert.Contains("#csharp and #csadvent", about);
        Assert.Contains("href=\"terms\"", about);
    }

    [Fact]
    public void SuggestedOtherAdventsAreWorkingHttpsLinks()
    {
        Assert.NotEmpty(OtherAdventsService.Suggestions);
        Assert.All(OtherAdventsService.Suggestions, advent => Assert.StartsWith("https://", advent.Url));
        Assert.DoesNotContain(OtherAdventsService.Suggestions, advent => advent.Url.Contains("golang.christmas") || advent.Url.Contains("pwadvent.dev"));
        Assert.Equal(OtherAdventsService.Suggestions.Count, OtherAdventsService.Suggestions.Select(a => a.Url).Distinct().Count());
    }
}
