using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Readers can ask for each December day's posts with their announcements (#206). Delivered through the announcements'
/// development endpoint: season 2351's December 3 has both posts ready, and season 2353's December 10 checks how they're
/// queued (#152). 9 AM Eastern is 14:00 UTC in December.
/// </summary>
public sealed partial class ReaderDailyEmailTests(AppHostFixture fixture)
{
    private const string Subject2351 = "Day 3 of C# Advent: 2 new posts to share";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadersWhoAskGetEachDaysPostsOnceAndCanTurnThemOff()
    {
        await EnsureSeasonAsync(2351, 3);
        const string reader = "reader-daily-2351@example.com";
        const string plain = "reader-plain-2351@example.com";
        await AddSubscriberAsync(plain, dailyPosts: false);

        // Asking for the daily posts is confirmed by the person who gets the email, like the list itself.
        using var browser = fixture.CreateBrowser();
        await SubscribeAsync(browser, reader);
        var confirmation = await fixture.WaitForEmailAsync(reader, "Confirm C# Advent's daily posts");
        var link = WebUtility.HtmlDecode(ConfirmLink().Match(confirmation.Html).Groups["path"].Value);
        var confirmed = await browser.PostAsync(link, null, CancellationToken);
        Assert.Contains("each day&#x27;s new posts from December 1 to 25", await browser.GetStringAsync(TestBrowser.PathOf(confirmed.Headers.Location!), CancellationToken));
        Assert.NotNull(await DailyPostsSinceAsync(reader));

        // A subscriber already on the list confirms them the same way.
        await SubscribeAsync(browser, plain);
        await fixture.WaitForEmailAsync(plain, "Confirm C# Advent's daily posts");
        Assert.Null(await DailyPostsSinceAsync(plain));

        // Not before the morning send time; then once, to the reader who asked.
        await RunAsync("2351-12-03T13:00:00Z");
        Assert.Equal(0, await DeliveriesAsync(2351, reader));
        await RunAsync("2351-12-03T14:00:00Z");
        var email = await fixture.WaitForEmailAsync(reader, Subject2351);
        Assert.Contains("href=\"https://localhost:7092/go/2351/3/1?via=email\"", email.Html);
        Assert.Contains("you asked for C# Advent's daily posts by email", email.Text);
        await RunAsync("2351-12-03T15:00:00Z");
        Assert.Equal(1, await DeliveriesAsync(2351, reader));
        Assert.Equal(0, await fixture.CountEmailsAsync(plain, Subject2351));

        // Its one-click link turns off only the daily posts, after a button press.
        var headers = await fixture.GetHeadersAsync(email);
        var stop = new Uri(StopLink().Match(email.Text).Groups["url"].Value);
        Assert.Equal($"<{stop}>", Assert.Single(headers["List-Unsubscribe"]));
        var opened = await browser.GetAsync(stop.PathAndQuery, CancellationToken);
        Assert.Contains("Stop the daily posts?", await browser.GetStringAsync(TestBrowser.PathOf(opened.Headers.Location!), CancellationToken));
        Assert.NotNull(await DailyPostsSinceAsync(reader));

        var stopped = await browser.PostAsync(stop.PathAndQuery, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]), CancellationToken);
        Assert.EndsWith("done=true", stopped.Headers.Location!.OriginalString);
        Assert.Contains("still on the announcements list", WebUtility.HtmlDecode(await browser.GetStringAsync(TestBrowser.PathOf(stopped.Headers.Location!), CancellationToken)));
        Assert.Null(await DailyPostsSinceAsync(reader));
    }

    [Fact]
    public async Task TheDailyPostsAreQueuedAsListMailForTheirDayOnly()
    {
        await EnsureSeasonAsync(2353, 10);
        const string reader = "reader-queue-2353@example.com";
        await AddSubscriberAsync(reader, dailyPosts: true);

        // Every reader's copy is queued at once; the queue sends them after the authors' daily email, as list mail.
        await RunAsync(new DateTimeOffset(2353, 12, 10, 15, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture));
        await fixture.WaitForEmailAsync(reader, "Day 10 of C# Advent: 2 new posts to share");
        Assert.Equal(1, await DeliveriesAsync(2353, reader));
        await fixture.WaitForQueueAsync(reader);

        await using var db = await fixture.CreateDbContextAsync();
        // Other tests' announcements can be queued to this subscriber too.
        var queued = await db.EmailOutbox.SingleAsync(e => e.ToAddress == reader && e.Kind == "daily-posts-readers", CancellationToken);
        Assert.Equal(("daily-posts-readers", EmailPriority.ReadersDailyPosts, "daily-posts:2353:10"), (queued.Kind, queued.Priority, queued.Reference));

        // Not sent by midnight Eastern, it would have expired rather than arrive a day late.
        Assert.Equal(new DateTimeOffset(2353, 12, 11, 5, 0, 0, TimeSpan.Zero), queued.SendBefore);
        Assert.Equal(QueuedEmailStatus.Sent, queued.Status);
    }

    private async Task RunAsync(string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/announcements/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Task EnsureSeasonAsync(int year, int day) => TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
    {
        foreach (var slot in season.Slots.Where(s => s.Day == day))
        {
            slot.AuthorName = $"Reader Author {slot.Part}";
            slot.PostTitle = $"Post {slot.Part} for readers in {year}";
            slot.PostUrl = $"https://example.com/readers-{year}-{slot.Part}";
        }
    });

    /// <summary>A subscriber who confirmed two days ago, so the form can send them a new confirmation.</summary>
    private async Task AddSubscriberAsync(string email, bool dailyPosts)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var twoDaysAgo = DateTimeOffset.UtcNow.AddDays(-2);
        db.AnnouncementSubscribers.Add(new AnnouncementSubscriber
        {
            Email = email,
            CreatedAt = twoDaysAgo,
            ConfirmationSentAt = twoDaysAgo,
            ConfirmedAt = twoDaysAgo,
            DailyPostsSince = dailyPosts ? twoDaysAgo : null,
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private static async Task SubscribeAsync(HttpClient browser, string email)
    {
        var html = await browser.GetStringAsync("/announcements", CancellationToken);
        Assert.Contains("name=\"DailyPosts\"", html);
        var subscribed = await browser.PostAsync("/announcements/subscribe", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Email"] = email,
            ["Website"] = "",
            ["ReturnUrl"] = "announcements",
            ["DailyPosts"] = "true",
        }), CancellationToken);
        Assert.Equal("/announcements?done=check-inbox", TestBrowser.PathOf(subscribed.Headers.Location!));
    }

    private async Task<DateTimeOffset?> DailyPostsSinceAsync(string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.AnnouncementSubscribers.Where(s => s.Email == email).Select(s => s.DailyPostsSince).SingleAsync(CancellationToken);
    }

    private async Task<int> DeliveriesAsync(int year, string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.DailyPostDeliveries.CountAsync(d => d.SeasonYear == year && d.Subscriber.Email == email, CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("href=\"https?://[^/\"]+(?<path>/emails/announcements/confirm\\?token=[^\"]+)\"")]
    private static partial Regex ConfirmLink();

    [GeneratedRegex(@"Unsubscribe: (?<url>\S+)")]
    private static partial Regex StopLink();
}
