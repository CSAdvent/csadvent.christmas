using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The announcements list (#49): signing up with a confirmed address through the plain, rate-limited form, and organizers'
/// announcements delivered to it. Uses season 2125 for the "sign-ups are open" email.
/// </summary>
public sealed partial class AnnouncementTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAddressIsConfirmedByThePersonWhoGetsTheEmail()
    {
        const string email = "Announce-Ann@Example.com";
        using var browser = fixture.CreateBrowser();

        var subscribed = await SubscribeAsync(browser, email);
        Assert.Equal("/announcements?done=check-inbox", TestBrowser.PathOf(subscribed.Headers.Location!));
        Assert.Null(await ConfirmedAtAsync("announce-ann@example.com"));

        // The link in the email only shows a button: opening it (as mail scanners do) confirms nothing.
        var confirmation = await fixture.WaitForEmailAsync("announce-ann@example.com", "Confirm your C# Advent announcements");
        var link = WebUtility.HtmlDecode(ConfirmLink().Match(confirmation.Html).Groups["path"].Value);
        var page = await browser.GetAsync(link, CancellationToken);
        Assert.StartsWith("/announcements/confirm?token=", TestBrowser.PathOf(page.Headers.Location!));
        Assert.Contains("Confirm my email", await browser.GetStringAsync(TestBrowser.PathOf(page.Headers.Location!), CancellationToken));
        Assert.Null(await ConfirmedAtAsync("announce-ann@example.com"));

        var confirmed = await browser.PostAsync(link, null, CancellationToken);
        Assert.EndsWith("&done=true", TestBrowser.PathOf(confirmed.Headers.Location!));
        Assert.NotNull(await ConfirmedAtAsync("announce-ann@example.com"));
        Assert.Contains("You're on the list", await browser.GetStringAsync(TestBrowser.PathOf(confirmed.Headers.Location!), CancellationToken));
    }

    [Fact]
    public async Task TheHomePageFormComesBackToTheHomePage()
    {
        using var browser = fixture.CreateBrowser();

        var subscribed = await SubscribeAsync(browser, "announce-home@example.com", returnTo: "home");

        Assert.Equal("/?done=check-inbox#announcements", TestBrowser.PathOf(subscribed.Headers.Location!));
    }

    [Fact]
    public async Task BotsAndBadAddressesGetNowhere()
    {
        using var browser = fixture.CreateBrowser();

        var bot = await SubscribeAsync(browser, "announce-bot@example.com", website: "https://spam.example");
        Assert.Equal("/announcements?done=check-inbox", TestBrowser.PathOf(bot.Headers.Location!));
        Assert.False(await ExistsAsync("announce-bot@example.com"));

        var bad = await SubscribeAsync(browser, "not an address");
        Assert.Equal("/announcements?error=email", TestBrowser.PathOf(bad.Headers.Location!));
    }

    [Fact]
    public async Task ALinkThatDoesntWorkSaysSo()
    {
        using var browser = fixture.CreateBrowser();

        Assert.Contains("This link doesn't work anymore", await browser.GetStringAsync("/announcements/confirm?token=forged", CancellationToken));
        Assert.Contains("This link doesn't work anymore", await browser.GetStringAsync("/announcements/unsubscribe?token=forged", CancellationToken));

        // A one-click unsubscribe with a bad token doesn't claim to have unsubscribed anyone.
        var stop = await browser.PostAsync("/emails/announcements/stop?token=forged", null, CancellationToken);
        Assert.DoesNotContain("done=true", TestBrowser.PathOf(stop.Headers.Location!));
    }

    [Fact]
    public async Task OrganizersTestAndSendAnnouncementsThatReachEachSubscriberOnce()
    {
        var reader = await ConfirmedSubscriberAsync("announce-reader@example.com");
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var saved = await organizer.PostFormAsync("/manage/announcements/new", "announcement", new()
        {
            ["Form.Subject"] = "An announcement for the readers",
            ["Form.Message"] = "First paragraph.\n\nSecond paragraph.",
            ["Form.ButtonLabel"] = "See the calendar",
            ["Form.ButtonUrl"] = "https://localhost:7092/",
        });
        var page = TestBrowser.PathOf(saved.Headers.Location!).Split('?')[0];
        Assert.Matches("^/manage/announcements/[0-9]+$", page);

        await organizer.PostFormAsync(page, "test", new());
        await fixture.WaitForEmailAsync("hello@csadvent.christmas", "[Test] An announcement for the readers");

        var sent = await organizer.PostFormAsync(page, "send", new() { ["Send.Confirm"] = "true" });
        Assert.Equal($"{page}?done=sent", TestBrowser.PathOf(sent.Headers.Location!));

        await RunAnnouncementsAsync();
        var email = await fixture.WaitForEmailAsync("announce-reader@example.com", "An announcement for the readers");
        Assert.Contains("Second paragraph.", email.Html);

        // A second run sends nothing more to the same subscriber.
        await RunAnnouncementsAsync();
        var id = int.Parse(page.Split('/')[^1], CultureInfo.InvariantCulture);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.AnnouncementDeliveries.CountAsync(d => d.AnnouncementId == id && d.SubscriberId == reader, CancellationToken));
        }

        // Its unsubscribe link deletes them, after a button press.
        using var browser = fixture.CreateBrowser();
        var stop = WebUtility.HtmlDecode(StopLink().Match(email.Html).Groups["path"].Value);
        Assert.StartsWith("/announcements/unsubscribe?token=", TestBrowser.PathOf((await browser.GetAsync(stop, CancellationToken)).Headers.Location!));
        Assert.Equal("/announcements/unsubscribe?done=true", TestBrowser.PathOf((await browser.PostAsync(stop, null, CancellationToken)).Headers.Location!));
        Assert.False(await ExistsAsync("announce-reader@example.com"));
    }

    [Fact]
    public async Task TheSignUpsEmailIsQueuedOnceWhenSignUpsOpen()
    {
        await TestSeasons.EnsureAsync(fixture, 2125, claimsOpen: true);

        await RunAnnouncementsAsync(year: 2125);
        await RunAnnouncementsAsync(year: 2125);

        await using var db = await fixture.CreateDbContextAsync();
        var queued = await db.Announcements.Where(a => a.Kind == AnnouncementKind.SignUpsOpen && a.SeasonYear == 2125).ToListAsync(CancellationToken);
        Assert.Equal("C# Advent 2125 sign-ups are open", Assert.Single(queued).Subject);
        Assert.NotNull(queued[0].SentAt);
    }

    /// <summary>Runs announcements at noon Eastern today, after the morning send time, whatever time the tests run.</summary>
    private async Task RunAnnouncementsAsync(int? year = null)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, eastern).Date;
        var noon = new DateTimeOffset(today.AddHours(12), eastern.GetUtcOffset(today.AddHours(12))).ToUniversalTime();
        using var client = fixture.CreateWebClient();
        var query = $"at={Uri.EscapeDataString(noon.ToString("O", CultureInfo.InvariantCulture))}{(year is { } y ? $"&year={y}" : "")}";
        var response = await client.PostAsync($"/dev/announcements/run?{query}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A subscriber who confirmed an hour ago, so they get anything sent from now on.</summary>
    private async Task<int> ConfirmedSubscriberAsync(string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var subscriber = new AnnouncementSubscriber { Email = email, CreatedAt = hourAgo, ConfirmationSentAt = hourAgo, ConfirmedAt = hourAgo };
        db.AnnouncementSubscribers.Add(subscriber);
        await db.SaveChangesAsync(CancellationToken);
        return subscriber.Id;
    }

    private async Task<HttpResponseMessage> SubscribeAsync(HttpClient browser, string email, string returnTo = "announcements", string website = "")
    {
        var html = await browser.GetStringAsync("/announcements", CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Email"] = email,
            ["Website"] = website,
            ["ReturnUrl"] = returnTo,
        };
        return await browser.PostAsync("/announcements/subscribe", new FormUrlEncodedContent(fields), CancellationToken);
    }

    private async Task<DateTimeOffset?> ConfirmedAtAsync(string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.AnnouncementSubscribers.Where(s => s.Email == email).Select(s => s.ConfirmedAt).SingleAsync(CancellationToken);
    }

    private async Task<bool> ExistsAsync(string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.AnnouncementSubscribers.AnyAsync(s => s.Email == email, CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("href=\"https?://[^/\"]+(?<path>/emails/announcements/confirm\\?token=[^\"]+)\"")]
    private static partial Regex ConfirmLink();

    [GeneratedRegex("href=\"https?://[^/\"]+(?<path>/emails/announcements/stop\\?token=[^\"]+)\"")]
    private static partial Regex StopLink();
}
