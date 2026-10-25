using System.Xml.Linq;

namespace CSAdvent.IntegrationTests;

/// <summary>The RSS feeds, read from the seeded history (2017-2025) plus the far-future test seasons, which aren't revealed.</summary>
public sealed class FeedTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheFeedHasEveryRevealedEntryFromEverySeasonNewestFirst()
    {
        // A future season's post must never appear before its reveal.
        await TestSeasons.EnsureAsync(fixture, 2114, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Future Fran";
            slot.PostTitle = "Not revealed until 2114";
            slot.PostUrl = "https://example.com/future";
        });
        using var client = fixture.CreateWebClient();

        var response = await client.GetAsync("/rss", CancellationToken);

        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType!.MediaType);
        var items = XDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).Root!.Element("channel")!.Elements("item").ToList();
        Assert.True(items.Count > 400, $"Only {items.Count} items.");
        Assert.DoesNotContain(items, i => i.Element("title")!.Value.Contains("2114", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Element("guid")!.Value.StartsWith("csadvent-2017-", StringComparison.Ordinal));
        // Published retrospectives are dated when they were published, so the newest entry may come after them.
        Assert.StartsWith("csadvent-2025-25-", items.First(i => !i.Element("guid")!.Value.EndsWith("-retrospective", StringComparison.Ordinal)).Element("guid")!.Value);

        var dates = items.Select(i => DateTimeOffset.Parse(i.Element("pubDate")!.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(dates.OrderByDescending(d => d), dates);
        Assert.Equal(items.Count, items.Select(i => i.Element("guid")!.Value).Distinct().Count());
        Assert.All(items, i => Assert.EndsWith("?via=rss", i.Element("link")!.Value));
    }

    [Fact]
    public async Task UnchangedFeedsCostA304()
    {
        using var client = fixture.CreateWebClient();

        // Other tests change the archive (authors' names, retrospectives) while this runs, so a changed feed between the two
        // requests is tried again: it only has to be unchanged once.
        var status = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 5 && status != HttpStatusCode.NotModified; attempt++)
        {
            var first = await client.GetAsync("/rss", CancellationToken);
            Assert.Equal("public, max-age=600", first.Headers.CacheControl!.ToString());

            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/rss");
            conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
            status = (await client.SendAsync(conditional, CancellationToken)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotModified, status);
    }

    [Fact]
    public async Task EachSeasonHasItsOwnFeedAndEveryPageLinksTheMainOne()
    {
        using var client = fixture.CreateWebClient();

        var season = XDocument.Parse(await client.GetStringAsync("/archive/2017/rss", CancellationToken)).Root!.Element("channel")!;
        Assert.Equal("C# Advent 2017", season.Element("title")!.Value);
        Assert.All(season.Elements("item"), i => Assert.StartsWith("csadvent-2017-", i.Element("guid")!.Value));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/archive/1999/rss", CancellationToken)).StatusCode);

        Assert.Contains("<link rel=\"alternate\" type=\"application/rss+xml\" title=\"C# Advent\" href=\"rss\"", await client.GetStringAsync("/", CancellationToken));
        Assert.Contains("href=\"archive/2017/rss\"", await client.GetStringAsync("/archive/2017", CancellationToken));
    }
}
