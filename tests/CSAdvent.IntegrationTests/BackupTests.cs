using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Backup posters, each scenario in its own season. Asks are driven through the development endpoint at chosen times
/// (6 PM Eastern is 23:00 UTC in December; midnight is 05:00 UTC). Pages run on the real clock, which is long before these
/// seasons, so anything they do happens "early": accepting a missing-link spot stands by until a run at the reveal.
/// </summary>
public sealed partial class BackupTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task JoiningShowsYourPlaceAndYouCanLeave()
    {
        await TestSeasons.EnsureAsync(fixture, 2112, claimsOpen: true);
        using var browser = await fixture.SignInAsync("bk-joiner", 6331, "Jo Iner");

        var form = await browser.GetStringAsync("/backups/2112", CancellationToken);
        Assert.Contains("value=\"Jo Iner\"", form);

        var joined = await browser.PostFormAsync("/backups/2112", "backup", new() { ["Form.Name"] = "Jo Iner", ["Form.AgreeToTerms"] = "true" });
        Assert.Equal("/backups/2112?done=joined", TestBrowser.PathOf(joined.Headers.Location!));
        Assert.Contains("You&#x27;re number 1 on the backup list.", await browser.GetStringAsync("/backups/2112", CancellationToken));
        await fixture.WaitForEmailAsync("bk-joiner@example.com", "You're on the C# Advent 2112 backup list");

        using (var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId))
        {
            var list = await organizer.GetStringAsync("/manage/backups?year=2112", CancellationToken);
            Assert.Contains("Jo Iner", list);
            Assert.Contains("bk-joiner@example.com", list);
        }

        await browser.PostFormAsync("/backups/2112", "leave", new() { ["Leave.Confirm"] = "true" });
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.Substitutes.AnyAsync(s => s.SeasonYear == 2112, CancellationToken));
    }

    [Fact]
    public async Task AMissingLinkGoesDownTheListAndTheBackupTakesOverAtTheReveal()
    {
        const int year = 2107;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 5, Part: 1 });
            slot.AuthorName = "Missing Mike";
            slot.Contact = new SlotContact { Email = "mike-2107@example.com" };
        });
        using var amy = await JoinAsync(year, "bk-amy", 6301, "Amy Backup");
        using var ben = await JoinAsync(year, "bk-ben", 6302, "Ben Backup");
        using var cal = await JoinAsync(year, "bk-cal", 6303, "Cal Backup");

        // Nothing before 6 PM Eastern the evening before; then the first backup is asked.
        Assert.Equal(new RunResult(0, 0, 0, 0), await RunAsync(year, "2107-12-04T22:59:00Z"));
        Assert.Equal(new RunResult(1, 0, 0, 0), await RunAsync(year, "2107-12-04T23:00:00Z"));
        var amyAsk = await fixture.WaitForEmailAsync("bk-amy@example.com", "Can you fill in on C# Advent, December 5?");
        Assert.Contains("There's no guarantee yet", amyAsk.Text);

        // Amy has 4 hours; then Ben is asked, declines, and the next run asks Cal.
        Assert.Equal(new RunResult(0, 0, 0, 0), await RunAsync(year, "2107-12-05T01:00:00Z"));
        Assert.Equal(new RunResult(1, 0, 0, 0), await RunAsync(year, "2107-12-05T03:00:00Z"));
        var benOffer = OfferPath(await fixture.WaitForEmailAsync("bk-ben@example.com", "Can you fill in on C# Advent, December 5?"));
        await ben.PostFormAsync(benOffer, "decline", new());
        Assert.Equal(new RunResult(1, 0, 0, 0), await RunAsync(year, "2107-12-05T03:15:00Z"));

        // Cal accepts, and stands by until the reveal.
        var calOffer = OfferPath(await fixture.WaitForEmailAsync("bk-cal@example.com", "Can you fill in on C# Advent, December 5?"));
        await cal.PostFormAsync(calOffer, "accept", new()
        {
            ["Form.AuthorName"] = "Cal Backup",
            ["Form.PostTitle"] = "A post Cal had ready",
            ["Form.PostUrl"] = "https://example.com/cal",
        });
        Assert.Contains("You&#x27;re on deck for December 5", await cal.GetStringAsync(calOffer, CancellationToken));
        Assert.Equal(new RunResult(0, 0, 0, 0), await RunAsync(year, "2107-12-05T04:59:00Z"));
        Assert.Equal(new RunResult(0, 1, 0, 0), await RunAsync(year, "2107-12-05T05:00:00Z"));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).SingleAsync(s => s.SeasonYear == year && s.Day == 5 && s.Part == 1, CancellationToken);
            Assert.Equal(("Cal Backup", "A post Cal had ready", "https://example.com/cal", "bk-cal@example.com"),
                (slot.AuthorName, slot.PostTitle, slot.PostUrl, slot.Contact!.Email));
        }

        await fixture.WaitForEmailAsync("bk-cal@example.com", "Your post is on the C# Advent calendar: December 5");
        await fixture.WaitForEmailAsync("hello@csadvent.christmas", "A backup poster filled C# Advent, December 5", containing: "December 5, 2107");
        await fixture.WaitForEmailAsync("mike-2107@example.com", "Your C# Advent spot went to a backup poster");

        // Amy never answered; by now someone else has stepped in.
        var amyPage = await amy.GetStringAsync(OfferPath(amyAsk), CancellationToken);
        Assert.Contains("Someone else stepped in", amyPage);
    }

    [Fact]
    public async Task ADroppedSpotIsOfferedStraightAwayAndTakenOnAcceptance()
    {
        const int year = 2109;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var author = await fixture.SignInAsync("bk-dropper", 6311);
        using var backup = await JoinAsync(year, "bk-dana", 6312, "Dana Backup");
        var slotId = await GiveSpotAsync(year, day: 10, part: 2, "Drop Dropper", githubUserId: 6311);

        await author.PostFormAsync($"/my-spots/{slotId}", "dropout", new() { ["DropOut.Confirm"] = "true" });

        // Asked straight away, with no waiting for the evening before.
        var ask = await fixture.WaitForEmailAsync("bk-dana@example.com", "Can you fill in on C# Advent, December 10?");
        Assert.Contains("had to drop out, so the spot is open", ask.Text);
        Assert.DoesNotContain("guarantee", ask.Text);

        var offer = OfferPath(ask);
        await backup.PostFormAsync(offer, "accept", new()
        {
            ["Form.AuthorName"] = "Dana Backup",
            ["Form.PostTitle"] = "Dana's stand-in post",
            ["Form.PostUrl"] = "https://example.com/dana",
        });

        Assert.Contains("Your post is on the calendar", await backup.GetStringAsync(offer, CancellationToken));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken);
            Assert.Equal(("Dana Backup", "https://example.com/dana"), (slot.AuthorName, slot.PostUrl));
            Assert.Null(slot.DroppedOutAt);
            Assert.Equal(slot.Id, (await db.Substitutes.SingleAsync(s => s.SeasonYear == year, CancellationToken)).AssignedSlotId);
        }

        // It's theirs now, on their My spots page.
        Assert.Contains($"href=\"my-spots/{slotId}\"", await backup.GetStringAsync("/my-spots", CancellationToken));
        await fixture.WaitForEmailAsync("bk-dana@example.com", "Your post is on the C# Advent calendar: December 10");
    }

    [Fact]
    public async Task WhenTheOriginalLinkArrivesTheBackupIsThankedAndTheAuthorKeepsTheSpot()
    {
        const int year = 2110;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 3, Part: 1 });
            slot.AuthorName = "Prompt Pam";
            slot.Contact = new SlotContact { Email = "pam-2110@example.com" };
        });
        using var backup = await JoinAsync(year, "bk-eve", 6321, "Eve Backup");

        Assert.Equal(new RunResult(1, 0, 0, 0), await RunAsync(year, "2110-12-02T23:00:00Z"));
        var offer = OfferPath(await fixture.WaitForEmailAsync("bk-eve@example.com", "Can you fill in on C# Advent, December 3?"));
        await backup.PostFormAsync(offer, "accept", new()
        {
            ["Form.AuthorName"] = "Eve Backup",
            ["Form.PostTitle"] = "Eve's post",
            ["Form.PostUrl"] = "https://example.com/eve",
        });

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 3 && s.Part == 1, CancellationToken);
            slot.PostTitle = "Pam's post";
            slot.PostUrl = "https://example.com/pam";
            await db.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(new RunResult(0, 0, 1, 0), await RunAsync(year, "2110-12-03T01:00:00Z"));
        await fixture.WaitForEmailAsync("bk-eve@example.com", "You're off the hook for C# Advent, December 3");
        Assert.Equal(new RunResult(0, 0, 0, 0), await RunAsync(year, "2110-12-03T05:00:00Z"));
        await using var check = await fixture.CreateDbContextAsync();
        Assert.Equal("Prompt Pam", (await check.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == 3 && s.Part == 1, CancellationToken)).AuthorName);
    }

    [Fact]
    public async Task TheOrganizersHearOnceWhenNobodyIsLeftToAsk()
    {
        const int year = 2111;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
            season.Slots.Single(s => s is { Day: 2, Part: 1 }).AuthorName = "Lonely Lou");

        Assert.Equal(new RunResult(0, 0, 0, 1), await RunAsync(year, "2111-12-01T23:00:00Z"));
        await fixture.WaitForEmailAsync("hello@csadvent.christmas", "No backup poster for C# Advent, December 2", containing: "December 2, 2111");
        Assert.Equal(new RunResult(0, 0, 0, 0), await RunAsync(year, "2111-12-01T23:15:00Z"));
    }

    private async Task<HttpClient> JoinAsync(int year, string login, long githubUserId, string name)
    {
        var browser = await fixture.SignInAsync(login, githubUserId, name);
        var response = await browser.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = name, ["Form.AgreeToTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    private async Task<int> GiveSpotAsync(int year, int day, int part, string author, long githubUserId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var contributorId = await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = author;
        slot.ContributorId = contributorId;
        await db.SaveChangesAsync(CancellationToken);
        return slot.Id;
    }

    private async Task<RunResult> RunAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/backups/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RunResult>(CancellationToken))!;
    }

    private static string OfferPath(MailPitMessage ask) => OfferLink().Match(ask.Text).Value;

    [GeneratedRegex(@"/backups/offers/\d+")]
    private static partial Regex OfferLink();

    private sealed record RunResult(int Asked, int Used, int Withdrawn, int Exhausted);
}
