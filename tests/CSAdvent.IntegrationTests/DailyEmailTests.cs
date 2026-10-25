using System.Net.Http.Json;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The daily email waits for both of the day's posts (#209), until the cutoff (2 PM Eastern, the second announcement), driven
/// through the development endpoint in December 2350 (9 AM Eastern is 14:00 UTC, and 2 PM is 19:00 UTC). Season 2350:
/// <list type="bullet">
/// <item>December 4: Ana's post is ready, and Ben's link arrives during the morning</item>
/// <item>December 6: Cal's post is ready, and Dee's never arrives</item>
/// <item>December 8: Eve's post is ready, and Fay's was found without the C# Advent blurb</item>
/// </list>
/// </summary>
public sealed class DailyEmailTests(AppHostFixture fixture)
{
    private const int Year = 2350;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDailyEmailWaitsUntilBothPostsAreReady()
    {
        await EnsureSeasonAsync();

        // 9 AM: Ben's link hasn't arrived, so nobody gets it yet.
        Assert.Equal(0, await DailySharesAsync("2350-12-04T14:00:00Z"));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ben = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 4 && s.Part == 2, CancellationToken);
            ben.PostTitle = "Ben's 2350 post";
            ben.PostUrl = "https://example.com/ben-2350";
            await db.SaveChangesAsync(CancellationToken);
        }

        // The next run sends it to all six authors, with both posts, once.
        Assert.Equal(6, await DailySharesAsync("2350-12-04T15:00:00Z"));
        var email = await fixture.WaitForEmailAsync("ana-2350@example.com", "Day 4 of C# Advent: 2 new posts to share");
        Assert.Contains("Ana&#x27;s 2350 post", email.Html);
        Assert.Contains("Ben&#x27;s 2350 post", email.Html);
        Assert.Equal(0, await DailySharesAsync("2350-12-04T16:00:00Z"));
    }

    [Fact]
    public async Task AtTheCutoffItGoesWithWhatsReady()
    {
        await EnsureSeasonAsync();

        Assert.Equal(0, await DailySharesAsync("2350-12-06T14:00:00Z"));
        Assert.Equal(0, await DailySharesAsync("2350-12-06T18:59:00Z"));

        // 2 PM, when the second post is announced: Cal's post goes out on its own, and only once.
        Assert.Equal(6, await DailySharesAsync("2350-12-06T19:00:00Z"));
        var email = await fixture.WaitForEmailAsync("dee-2350@example.com", "Day 6 of C# Advent: Cal's 2350 post");
        Assert.Contains("by Cal Alone", email.Text);
        Assert.Equal(0, await DailySharesAsync("2350-12-06T20:00:00Z"));
    }

    [Fact]
    public async Task APostWithoutTheBlurbIsWaitedForAndThenLeftOut()
    {
        await EnsureSeasonAsync();
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var fay = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 8 && s.Part == 2, CancellationToken);
            if (!await db.BlurbChecks.AnyAsync(c => c.SlotId == fay.Id, CancellationToken))
            {
                db.BlurbChecks.Add(new BlurbCheck { SlotId = fay.Id, PostUrl = fay.PostUrl!, Status = BlurbStatus.Missing, StatusSince = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(CancellationToken);
            }
        }

        Assert.Equal(0, await DailySharesAsync("2350-12-08T14:00:00Z"));
        Assert.Equal(6, await DailySharesAsync("2350-12-08T19:00:00Z"));
        var email = await fixture.WaitForEmailAsync("eve-2350@example.com", "Day 8 of C# Advent: Eve's 2350 post");
        Assert.DoesNotContain("Fay&#x27;s 2350 post", email.Html);
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
        void Claim(int day, int part, string author, string? title)
        {
            var name = author.Split(' ')[0].ToLowerInvariant();
            var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
            slot.AuthorName = author;
            slot.PostTitle = title;
            slot.PostUrl = title is null ? null : $"https://example.com/{name}-2350";
            slot.Contact = new SlotContact { Email = $"{name}-2350@example.com" };
        }

        Claim(4, 1, "Ana Early", "Ana's 2350 post");
        Claim(4, 2, "Ben Later", null);
        Claim(6, 1, "Cal Alone", "Cal's 2350 post");
        Claim(6, 2, "Dee Never", null);
        Claim(8, 1, "Eve Ready", "Eve's 2350 post");
        Claim(8, 2, "Fay Blurbless", "Fay's 2350 post");
    });

    private sealed record RunResult(int PostLinkReminders, int DailyShares, int DayOfReminders = 0);
}
