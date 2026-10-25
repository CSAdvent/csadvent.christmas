using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Optional emails can unsubscribe someone from all of them, while emails about what they signed up for (their spots, the
/// backup list) still come and have no unsubscribe link. Season 2129: Ada has a ready post on December 3 and no link yet for
/// December 5; Ben's post is ready on December 6.
/// </summary>
public sealed partial class EmailOptOutTests(AppHostFixture fixture)
{
    private const int Year = 2129;
    private const string Address = "optout-ada@example.com";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnsubscribingStopsOptionalEmailsButNotOnesAboutYourSpots()
    {
        await EnsureSeasonAsync();
        using var ada = await fixture.SignInAsync("optout-ada", 8701);
        using var reader = fixture.CreateBrowser();

        // An optional email (confirming announcements) has the link that unsubscribes from all of them.
        await SubscribeAsync(reader, Address);
        var confirmation = await fixture.WaitForEmailAsync(Address, "Confirm your C# Advent announcements");
        var link = UnsubscribeLink().Match(confirmation.Text).Groups["url"].Value;
        Assert.StartsWith("https://localhost:7092/emails/stop?token=", link);
        Assert.Equal($"<{link}>", (await fixture.GetHeadersAsync(confirmation))["List-Unsubscribe"].Single());

        // Opening it only asks; the button (or a mail provider's one-click POST) unsubscribes, and it can be undone.
        var token = Uri.EscapeDataString(Uri.UnescapeDataString(new Uri(link).Query.Split("token=")[1]));
        var page = $"/emails/unsubscribe?token={token}";
        Assert.Contains("Unsubscribe from C# Advent's emails?", WebUtility.HtmlDecode(await reader.GetPageAsync(page)));
        Assert.False(await IsOptedOutAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await reader.PostAsync($"/emails/stop?token={token}", null, CancellationToken)).StatusCode);
        Assert.True(await IsOptedOutAsync());
        Assert.Contains("we'll still email you about it", WebUtility.HtmlDecode(await reader.GetPageAsync(page)));
        await reader.PostAsync($"/emails/resume?token={token}", null, CancellationToken);
        Assert.False(await IsOptedOutAsync());
        await reader.PostAsync($"/emails/stop?token={token}", null, CancellationToken);

        // Emails about what they signed up for still come, without an unsubscribe link.
        var joined = await ada.PostFormAsync($"/backups/{Year}", "backup", new() { ["Form.Name"] = "Ada Optout", ["Form.AgreeToTerms"] = "true" });
        Assert.Equal($"/backups/{Year}?done=joined", TestBrowser.PathOf(joined.Headers.Location!));
        var backup = await fixture.WaitForEmailAsync(Address, "You're on the C# Advent 2129 backup list");
        Assert.DoesNotContain("Unsubscribe", backup.Text);
        Assert.False((await fixture.GetHeadersAsync(backup)).ContainsKey("List-Unsubscribe"));

        // December 3, after the daily email's 2 PM cutoff: a reminder about her December 5 spot goes out; the daily email doesn't.
        await RunRemindersAsync("2129-12-03T19:30:00Z");
        var reminder = await fixture.WaitForEmailAsync(Address, "Your C# Advent post is due in 2 days (December 5)");
        Assert.DoesNotContain("Unsubscribe", reminder.Text);
        Assert.Equal(0, await fixture.CountEmailsAsync(Address, "Day 3 of C# Advent"));

        // Her profile says so, and turns the optional emails back on: December 6's daily email arrives.
        var profile = await ada.GetPageAsync("/profile");
        Assert.Contains("You've unsubscribed from C# Advent's optional emails", WebUtility.HtmlDecode(profile));
        var resumed = await ada.PostAsync("/profile/emails/resume", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(profile).Groups["token"].Value),
        }), CancellationToken);
        Assert.Equal("/profile?done=emails-on", TestBrowser.PathOf(resumed.Headers.Location!));
        Assert.False(await IsOptedOutAsync());

        await RunRemindersAsync("2129-12-06T19:30:00Z");
        await fixture.WaitForEmailAsync(Address, "Day 6 of C# Advent: Ben's 2129 post");
    }

    [Fact]
    public async Task ABrokenLinkUnsubscribesNobody()
    {
        using var reader = fixture.CreateBrowser();

        Assert.Contains("This link doesn't work", WebUtility.HtmlDecode(await reader.GetPageAsync("/emails/unsubscribe?token=forged")));
        Assert.Equal(HttpStatusCode.Redirect, (await reader.PostAsync("/emails/stop?token=forged", null, CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await reader.PostAsync("/emails/resume", null, CancellationToken)).StatusCode);
    }

    private Task EnsureSeasonAsync() => TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true, season =>
    {
        void Claim(int day, string author, string email, string? title)
        {
            var slot = season.Slots.Single(s => s.Day == day && s.Part == 1);
            slot.AuthorName = author;
            slot.PostTitle = title;
            slot.PostUrl = title is null ? null : $"https://example.com/{day}";
            slot.Contact = new SlotContact { Email = email };
        }

        Claim(3, "Ada Optout", Address, "Ada's 2129 post");
        Claim(5, "Ada Optout", Address, null);
        Claim(6, "Ben Other", "ben-2129@example.com", "Ben's 2129 post");
    });

    private static async Task SubscribeAsync(HttpClient browser, string email)
    {
        var html = await browser.GetStringAsync("/announcements", CancellationToken);
        await browser.PostAsync("/announcements/subscribe", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Email"] = email,
            ["Website"] = "",
            ["ReturnUrl"] = "announcements",
        }), CancellationToken);
    }

    private async Task RunRemindersAsync(string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/reminders/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

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
