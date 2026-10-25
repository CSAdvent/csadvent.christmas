using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Every email to a person can unsubscribe them from everything, and then nothing more is sent until they turn emails back
/// on. Joining season 2129's backup list (and leaving, and joining again) sends the emails.
/// </summary>
public sealed partial class EmailOptOutTests(AppHostFixture fixture)
{
    private const int Year = 2129;
    private const string Address = "optout-ada@example.com";
    private const string Joined = "You're on the C# Advent 2129 backup list";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnyEmailCanUnsubscribeSomeoneFromEverythingUntilTheyTurnItBackOn()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        using var ada = await fixture.SignInAsync("optout-ada", 8701);
        using var reader = fixture.CreateBrowser();

        await JoinAsync(ada);
        var email = await fixture.WaitForEmailAsync(Address, Joined);
        var link = UnsubscribeLink().Match(email.Text).Groups["url"].Value;
        Assert.StartsWith("https://localhost:7092/emails/stop?token=", link);
        Assert.Contains(link, email.Html);
        Assert.Equal($"<{link}>", (await fixture.GetHeadersAsync(email))["List-Unsubscribe"].Single());

        // Opening the link only asks; the button (or a mail provider's one-click POST) unsubscribes.
        var token = Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]);
        var page = $"/emails/unsubscribe?token={Uri.EscapeDataString(token)}";
        Assert.Contains("Unsubscribe from C# Advent's emails?", WebUtility.HtmlDecode(await reader.GetPageAsync(page)));
        Assert.False(await IsOptedOutAsync());

        var stopped = await reader.PostAsync($"/emails/stop?token={Uri.EscapeDataString(token)}", null, CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, stopped.StatusCode);
        Assert.True(await IsOptedOutAsync());
        Assert.Contains("You're unsubscribed", WebUtility.HtmlDecode(await reader.GetPageAsync(page)));

        // Changed their mind, then unsubscribed again.
        await reader.PostAsync($"/emails/resume?token={Uri.EscapeDataString(token)}", null, CancellationToken);
        Assert.False(await IsOptedOutAsync());
        await reader.PostAsync($"/emails/stop?token={Uri.EscapeDataString(token)}", null, CancellationToken);

        // Nothing more is sent.
        await LeaveAsync(ada);
        await JoinAsync(ada);
        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken);
        Assert.Equal(1, await fixture.CountEmailsAsync(Address, Joined));

        // Their profile says so, and turns emails back on.
        var profile = await ada.GetPageAsync("/profile");
        Assert.Contains("You've unsubscribed from C# Advent's emails", WebUtility.HtmlDecode(profile));
        var resumed = await ada.PostAsync("/profile/emails/resume", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(profile).Groups["token"].Value),
        }), CancellationToken);
        Assert.Equal("/profile?done=emails-on", TestBrowser.PathOf(resumed.Headers.Location!));
        Assert.False(await IsOptedOutAsync());

        await LeaveAsync(ada);
        await JoinAsync(ada);
        for (var attempt = 0; attempt < 50 && await fixture.CountEmailsAsync(Address, Joined) < 2; attempt++)
        {
            await Task.Delay(200, CancellationToken);
        }

        Assert.Equal(2, await fixture.CountEmailsAsync(Address, Joined));
    }

    [Fact]
    public async Task ABrokenLinkUnsubscribesNobody()
    {
        using var reader = fixture.CreateBrowser();

        Assert.Contains("This link doesn't work", WebUtility.HtmlDecode(await reader.GetPageAsync("/emails/unsubscribe?token=forged")));
        var stopped = await reader.PostAsync("/emails/stop?token=forged", null, CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, stopped.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await reader.PostAsync("/emails/resume", null, CancellationToken)).StatusCode);
    }

    private static async Task JoinAsync(HttpClient browser)
    {
        var joined = await browser.PostFormAsync($"/backups/{Year}", "backup", new() { ["Form.Name"] = "Ada Optout", ["Form.AgreeToTerms"] = "true" });
        Assert.Equal($"/backups/{Year}?done=joined", TestBrowser.PathOf(joined.Headers.Location!));
    }

    private static Task LeaveAsync(HttpClient browser) =>
        browser.PostFormAsync($"/backups/{Year}", "leave", new() { ["Leave.Confirm"] = "true" });

    private async Task<bool> IsOptedOutAsync()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var fingerprint = EmailOptOutLinks.Fingerprint(Address);
        return await db.EmailOptOuts.AnyAsync(o => o.Fingerprint == fingerprint, CancellationToken);
    }

    [GeneratedRegex(@"Unsubscribe: (?<url>\S+)")]
    private static partial Regex UnsubscribeLink();

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
