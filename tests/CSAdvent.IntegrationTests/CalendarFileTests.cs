using System.Text.RegularExpressions;
using CSAdvent.Web.Data;

namespace CSAdvent.IntegrationTests;

/// <summary>Add-to-calendar files (#207), for season 2334 (sign-ups scheduled) and season 2335 (not scheduled yet).</summary>
public sealed partial class CalendarFileTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASeasonHasFilesForItsSignUpsAndItsStart()
    {
        await EnsureScheduledSeasonAsync();
        using var reader = fixture.CreateWebClient();

        var signUps = await reader.GetAsync("/calendar/2334/sign-ups.ics", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signUps.StatusCode);
        Assert.Equal("text/calendar", signUps.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", signUps.Content.Headers.ContentType.CharSet);
        Assert.Equal("csadvent-2334-sign-ups.ics", signUps.Content.Headers.ContentDisposition!.FileNameStar ?? signUps.Content.Headers.ContentDisposition.FileName);
        var file = await signUps.Content.ReadAsStringAsync(CancellationToken);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", file);
        Assert.Contains("\r\nUID:csadvent-2334-sign-ups@localhost\r\n", file);
        Assert.Contains("\r\nDTSTART:23341101T140000Z\r\n", file);
        Assert.Contains("\r\nSUMMARY:C# Advent 2334: sign-ups open\r\n", file);

        // Midnight Eastern on December 1.
        var starts = await reader.GetStringAsync("/calendar/2334/starts.ics", CancellationToken);
        Assert.Contains("\r\nDTSTART:23341201T050000Z\r\n", starts);
        Assert.Contains("\r\nSUMMARY:C# Advent 2334 starts\r\n", starts);
    }

    [Fact]
    public async Task SignUpsHaveNoFileUntilTheyreScheduledAndTheTestSeasonHasNone()
    {
        await TestSeasons.EnsureAsync(fixture, 2335, claimsOpen: false);
        using var reader = fixture.CreateWebClient();

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/calendar/2335/sign-ups.ics", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/calendar/2335/starts.ics", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/calendar/1999/starts.ics", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/calendar/{Season.TestYear}/starts.ics", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/calendar/{Season.TestYear}/sign-ups.ics", CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TheAnnouncementsPageLinksTheNextSeasonsStart()
    {
        await EnsureScheduledSeasonAsync();
        using var reader = fixture.CreateWebClient();

        var page = await reader.GetPageAsync("/announcements");

        // Other tests' seasons come before 2334, so the next season is one of theirs.
        var link = StartsLink().Match(page);
        Assert.True(link.Success, "The announcements page doesn't link a season's start.");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/{link.Value}", CancellationToken)).StatusCode);
        Assert.DoesNotContain($"calendar/{Season.TestYear}", page);
    }

    private Task EnsureScheduledSeasonAsync() => TestSeasons.EnsureAsync(fixture, 2334, claimsOpen: false, season =>
        season.ClaimsOpenAt = new DateTimeOffset(2334, 11, 1, 14, 0, 0, TimeSpan.Zero));

    [GeneratedRegex(@"calendar/\d{4}/starts\.ics")]
    private static partial Regex StartsLink();
}
