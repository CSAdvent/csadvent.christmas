using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Signing up for announcements (#49): a confirmed address, through the plain, rate-limited form.</summary>
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
}
