using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The authors' daily email's unsubscribe link (#199): <c>/emails/daily/stop</c>, which mail providers also POST to for
/// one-click unsubscribe. Season 2620: Ola has spots on December 3 and 4 (under one address in two spellings), and Pia has
/// the other post each day. 10 AM Eastern is 15:00 UTC in December.
/// </summary>
public sealed partial class DailyEmailOptOutTests(AppHostFixture fixture)
{
    private const int Year = 2620;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLinkStopsTheDailyEmailsForEverySpotOfTheirsInTheSeason()
    {
        await EnsureSeasonAsync();

        // December 3's daily email goes to both authors, with the link in its text and in its one-click header.
        Assert.Equal(2, await DailySharesAsync("2620-12-03T15:00:00Z"));
        var email = await fixture.WaitForEmailAsync("ola-2620@example.com", "Day 3 of C# Advent: 2 new posts to share");
        var stop = new Uri(StopLink().Match(email.Text).Value);
        Assert.Equal($"<{stop}>", Assert.Single((await fixture.GetHeadersAsync(email))["List-Unsubscribe"]));

        // Opening it, as a mail scanner checking links does, only asks.
        using var browser = fixture.CreateBrowser();
        var opened = await browser.GetAsync(stop.PathAndQuery, CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
        var confirm = TestBrowser.PathOf(opened.Headers.Location!);
        Assert.StartsWith("/emails/daily/unsubscribe?token=", confirm);
        Assert.Contains("Stop the daily emails?", await browser.GetPageAsync(confirm));
        Assert.Empty(await OptedOutAsync());

        // The page's button, or a mail provider's one-click POST (no antiforgery token: the link's token is what counts),
        // turns them off for both of Ola's spots, whichever spelling of her address each has, and nobody else's.
        var stopped = await browser.PostAsync(stop.PathAndQuery, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]), CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, stopped.StatusCode);
        Assert.EndsWith("&done=true", TestBrowser.PathOf(stopped.Headers.Location!));
        Assert.Contains("You won't get the daily emails anymore", WebUtility.HtmlDecode(await browser.GetPageAsync(TestBrowser.PathOf(stopped.Headers.Location!))));
        Assert.Equal([3, 4], await OptedOutAsync());

        // So December 4's goes to Pia alone.
        Assert.Equal(1, await DailySharesAsync("2620-12-04T15:00:00Z"));
        await fixture.WaitForEmailAsync("pia-2620@example.com", "Day 4 of C# Advent: 2 new posts to share");
        Assert.Equal(0, await fixture.CountEmailsAsync("ola-2620@example.com", "Day 4 of C# Advent"));
    }

    [Fact]
    public async Task ALinkThatDoesntWorkSaysSoAndStopsNothing()
    {
        await EnsureSeasonAsync();
        var before = await OptedOutAsync();
        using var browser = fixture.CreateBrowser();

        var forged = await browser.PostAsync("/emails/daily/stop?token=forged", null, CancellationToken);

        Assert.Equal("/emails/daily/unsubscribe?token=forged&done=false", TestBrowser.PathOf(forged.Headers.Location!));
        Assert.Contains("This link doesn't work anymore", WebUtility.HtmlDecode(await browser.GetPageAsync("/emails/daily/unsubscribe?token=forged")));
        Assert.Equal(before, await OptedOutAsync());
    }

    /// <summary>The days of the season's spots whose author has turned the daily emails off.</summary>
    private async Task<List<int>> OptedOutAsync()
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.SlotContacts
            .Where(c => c.Slot.SeasonYear == Year && c.DailyEmailsOptedOutAt != null)
            .OrderBy(c => c.Slot.Day)
            .Select(c => c.Slot.Day)
            .ToListAsync(CancellationToken);
    }

    private async Task<int> DailySharesAsync(string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/reminders/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResult>(CancellationToken))!.DailyShares;
    }

    private Task EnsureSeasonAsync() => TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
    {
        void Claim(int day, int part, string author, string email)
        {
            var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
            var name = author.Split(' ')[0].ToLowerInvariant();
            slot.AuthorName = author;
            slot.PostTitle = $"{author}'s post for December {day}";
            slot.PostUrl = $"https://example.com/{name}-{Year}-{day}";
            slot.Contact = new SlotContact { Email = email };
        }

        Claim(3, 1, "Ola Optout", "ola-2620@example.com");
        Claim(3, 2, "Pia Stays", "pia-2620@example.com");
        Claim(4, 1, "Ola Optout", "Ola-2620@Example.com");
        Claim(4, 2, "Pia Stays", "pia-2620@example.com");
    });

    [GeneratedRegex(@"https?://[^\s""<>]+/emails/daily/stop\?token=[^\s""<>]+")]
    private static partial Regex StopLink();

    private sealed record RunResult(int PostLinkReminders, int DailyShares, int DayOfReminders = 0);
}
