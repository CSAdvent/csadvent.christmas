using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Reminder runs, driven through the development endpoint at chosen times in December 2106 (reminders start at 9 AM
/// Eastern, which is 14:00 UTC). Season 2106:
/// <list type="bullet">
/// <item>December 3 has two ready posts, by Rita and Sam</item>
/// <item>December 8 (Lucy) and December 10 (Olivia, who turned off the daily emails) have no post link</item>
/// <item>Tom has two spots, December 20 and 21, both ready</item>
/// </list>
/// </summary>
public sealed partial class ReminderTests(AppHostFixture fixture)
{
    private const int Year = 2106;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PostLinkRemindersGoOutOnTheChosenDaysOnce()
    {
        await EnsureSeasonAsync();

        // 8 AM Eastern is too early.
        Assert.Equal(new RunResult(0, 0), await RunAsync("2106-12-01T13:00:00Z"));

        // December 1 is 7 days before Lucy's day. There are no posts on December 1 to share.
        Assert.Equal(new RunResult(1, 0), await RunAsync("2106-12-01T15:00:00Z"));
        var reminder = await fixture.WaitForEmailAsync("lucy-2106@example.com", "Your C# Advent post is due in 7 days (December 8)");
        Assert.Contains("Your day is 7 days away!", reminder.Html);
        Assert.Contains("/my-spots", reminder.Html);
        Assert.Equal("hello@csadvent.christmas", Assert.Single(reminder.ReplyTo).Address);

        // Later runs that day send nothing new, and December 2 (6 days before) isn't a reminder day.
        Assert.Equal(new RunResult(0, 0), await RunAsync("2106-12-01T19:00:00Z"));
        Assert.Equal(new RunResult(0, 0), await RunAsync("2106-12-02T15:00:00Z"));
    }

    [Fact]
    public async Task AnAuthorWithoutALinkOnTheirDayGetsALastCallBeforeTheAnnouncement()
    {
        await EnsureSeasonAsync();

        // Lucy's post 1 on December 8 is announced at 10 AM Eastern, so her last call goes out from 7 AM.
        Assert.Equal(new RunResult(0, 0, 0), await RunAsync("2106-12-08T11:30:00Z"));
        Assert.Equal(new RunResult(0, 0, 1), await RunAsync("2106-12-08T12:30:00Z"));
        var lastCall = await fixture.WaitForEmailAsync("lucy-2106@example.com", "Today's your C# Advent day: add your post's link by 10 AM Eastern");
        Assert.Contains("/my-spots", lastCall.Html);

        // Once only.
        Assert.Equal(new RunResult(0, 0, 0), await RunAsync("2106-12-08T13:00:00Z"));
    }

    [Fact]
    public async Task TheDailyEmailGoesToEveryAuthorOnceAndCanBeTurnedOff()
    {
        await EnsureSeasonAsync();

        // December 3: reminders for Lucy (5 days) and Olivia (7 days), and the daily email to Lucy, Rita, Sam and Tom (once).
        Assert.Equal(new RunResult(2, 4), await RunAsync("2106-12-03T15:00:00Z"));

        const string subject = "Day 3 of C# Advent: 2 new posts to share";
        var rita = await fixture.WaitForEmailAsync("rita-2106@example.com", subject);
        Assert.Contains("One of today&#x27;s posts is yours", rita.Html);
        Assert.Contains("href=\"https://localhost:7092/go/2106/3/2?via=email\"", rita.Html);
        var lucy = await fixture.WaitForEmailAsync("lucy-2106@example.com", subject);
        Assert.DoesNotContain("is yours", lucy.Html);
        Assert.Equal(1, await fixture.CountEmailsAsync("tom-2106@example.com", subject));
        Assert.Equal(0, await fixture.CountEmailsAsync("olivia-2106@example.com", subject));
        await fixture.WaitForEmailAsync("olivia-2106@example.com", "Your C# Advent post is due in 7 days (December 10)");

        // One-click unsubscribe for mail providers, and the same link in the footer.
        var headers = await fixture.GetHeadersAsync(lucy);
        Assert.Equal("List-Unsubscribe=One-Click", Assert.Single(headers["List-Unsubscribe-Post"]));
        var link = new Uri(StopLink().Match(lucy.Text).Groups["url"].Value);
        Assert.Equal($"<{link}>", Assert.Single(headers["List-Unsubscribe"]));

        // Opening the link only asks; posting to it opts out.
        using var browser = fixture.CreateBrowser();
        var opened = await browser.GetAsync(link.PathAndQuery, CancellationToken);
        var confirm = await browser.GetStringAsync(TestBrowser.PathOf(opened.Headers.Location!), CancellationToken);
        Assert.Contains("Stop the daily emails?", confirm);
        Assert.Null(await OptedOutAtAsync("lucy-2106@example.com"));

        var stopped = await browser.PostAsync(link.PathAndQuery, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]), CancellationToken);
        Assert.EndsWith("done=true", stopped.Headers.Location!.OriginalString);
        Assert.Contains("You won't get the daily emails anymore", await browser.GetStringAsync(TestBrowser.PathOf(stopped.Headers.Location!), CancellationToken));
        Assert.NotNull(await OptedOutAtAsync("lucy-2106@example.com"));
    }

    [Fact]
    public async Task ABrokenUnsubscribeLinkSaysSo()
    {
        using var browser = fixture.CreateBrowser();

        var page = await browser.GetStringAsync("/emails/daily/unsubscribe?token=not-a-real-token", CancellationToken);

        Assert.Contains("This link doesn't work anymore", page);
    }

    private async Task<RunResult> RunAsync(string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/reminders/run?at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResult>(CancellationToken))!;
    }

    private async Task<DateTimeOffset?> OptedOutAtAsync(string email)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.SlotContacts.Where(c => c.Email == email).Select(c => c.DailyEmailsOptedOutAt).SingleAsync(CancellationToken);
    }

    private Task EnsureSeasonAsync() => TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
    {
        void Claim(int day, int part, string author, string email, string? title = null, DateTimeOffset? optedOut = null)
        {
            var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
            slot.AuthorName = author;
            slot.PostTitle = title;
            slot.PostUrl = title is null ? null : $"https://example.com/{author.Split(' ')[0].ToLowerInvariant()}";
            slot.Contact = new SlotContact { Email = email, DailyEmailsOptedOutAt = optedOut };
        }

        Claim(3, 1, "Rita Ready", "rita-2106@example.com", "Rita's 2106 post");
        Claim(3, 2, "Sam Second", "sam-2106@example.com", "Sam's 2106 post");
        Claim(8, 1, "Lucy Late", "lucy-2106@example.com");
        Claim(10, 1, "Olivia Optout", "olivia-2106@example.com", optedOut: DateTimeOffset.UtcNow);
        Claim(20, 1, "Tom Twice", "tom-2106@example.com", "Tom's first 2106 post");
        Claim(21, 2, "Tom Twice", "tom-2106@example.com", "Tom's second 2106 post");
    });

    [GeneratedRegex(@"Unsubscribe: (?<url>\S+)")]
    private static partial Regex StopLink();

    private sealed record RunResult(int PostLinkReminders, int DailyShares, int DayOfReminders = 0);
}
