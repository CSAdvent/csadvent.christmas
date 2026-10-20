using CSAdvent.Web.Data;

namespace CSAdvent.IntegrationTests;

public sealed class HomePageTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShowsTheCurrentSeasonWithClaimedAndOpenSpots()
    {
        // 2089 is earlier than any season the other tests create, so it's the current season: the earliest that isn't over.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = Season.Create(2089, slotsPerDay: 2, DateTimeOffset.UtcNow);
            season.ClaimsOpenAt = DateTimeOffset.UtcNow.AddDays(-1);
            var slot = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Grace Hopper";
            slot.AuthorUrl = "https://example.com/grace";
            db.Seasons.Add(season);
            await db.SaveChangesAsync(CancellationToken);
        }

        using var client = fixture.CreateWebClient();
        var response = await client.GetAsync("/", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains(">C# Advent 2089</h1>", html);
        Assert.Contains("49 of 50 spots are still available", html);
        Assert.Contains("Grace Hopper", html);
        Assert.Contains("href=\"claim/2089/1/2\"", html);
        Assert.DoesNotContain("href=\"claim/2089/1/1\"", html);
    }
}
