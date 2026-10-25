using System.Xml.Linq;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Posts' one-line summaries (#208): added on My spots in season 2330, which isn't revealed, so nothing shows them, and by an
/// organizer on an early-December 2019 archive post (no other test changes those), which shows them everywhere.
/// </summary>
public sealed class PostSummaryTests(AppHostFixture fixture)
{
    private const int Year = 2330;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAuthorsSummaryIsSavedButNeverShownBeforeTheReveal()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        using var author = await fixture.SignInAsync("summary-sue", 233001);
        var slotId = await GiveSpotAsync(233001, day: 5, part: 1);

        var tooLong = await author.PostFormAsync($"/my-spots/{slotId}", "spot", new()
        {
            ["Form.AuthorName"] = "Sue Summary",
            ["Form.PostSummary"] = new string('a', 161),
        });
        Assert.Contains("Keep the summary to 160 characters or fewer.", await tooLong.Content.ReadAsStringAsync(CancellationToken));

        var saved = await author.PostFormAsync($"/my-spots/{slotId}", "spot", new()
        {
            ["Form.AuthorName"] = "Sue Summary",
            ["Form.PostTitle"] = "A secret 2330 title",
            ["Form.PostUrl"] = "https://example.com/secret-2330",
            ["Form.PostSummary"] = "  Quokkas   explain\n source generators ",
        });
        Assert.Equal($"/my-spots/{slotId}?done=saved", TestBrowser.PathOf(saved.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal("Quokkas explain source generators", (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).PostSummary);
        }

        Assert.Contains("value=\"Quokkas explain source generators\"", await author.GetPageAsync($"/my-spots/{slotId}"));

        using var reader = fixture.CreateWebClient();
        var page = await reader.GetPageAsync($"/{Year}/day/5/1");
        Assert.Contains("Coming on December 5", page);
        Assert.DoesNotContain("Quokkas", page);
        Assert.DoesNotContain("Quokkas", await reader.GetStringAsync("/rss", CancellationToken));
        Assert.DoesNotContain("Quokkas", await reader.GetStringAsync($"/archive/{Year}/rss", CancellationToken));
    }

    [Fact]
    public async Task AnOrganizersSummaryShowsOnTheEntryPageAndInTheFeed()
    {
        Slot slot;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slot = await db.Slots
                .AsNoTracking()
                .Where(s => s.SeasonYear == 2019 && s.Day >= 2 && s.Day <= 9 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
                .OrderBy(s => s.Day).ThenBy(s => s.Part)
                .FirstAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var fields = new Dictionary<string, string>
        {
            ["Form.AuthorName"] = slot.AuthorName!,
            ["Form.PostTitle"] = slot.PostTitle!,
            ["Form.PostUrl"] = slot.PostUrl!,
            ["Form.PostSummary"] = "Wombats tour the 2019 archive",
        };
        if (slot.AuthorUrl is { } authorUrl)
        {
            fields["Form.AuthorUrl"] = authorUrl;
        }

        var saved = await organizer.PostFormAsync($"/manage/entries/{slot.Id}", "entry", fields);
        Assert.True(saved.Headers.Location is not null, await saved.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal($"/manage/entries/{slot.Id}?done=saved", TestBrowser.PathOf(saved.Headers.Location));

        using var reader = fixture.CreateWebClient();
        var page = await reader.GetPageAsync($"/2019/day/{slot.Day}/{slot.Part}");
        Assert.Contains("<p class=\"mt-3 text-xl text-slate-600\">Wombats tour the 2019 archive</p>", page);
        Assert.Contains("<meta name=\"description\" content=\"Wombats tour the 2019 archive. By ", page);

        var item = XDocument.Parse(await reader.GetStringAsync("/archive/2019/rss", CancellationToken)).Root!.Element("channel")!.Elements("item")
            .Single(i => i.Element("guid")!.Value == $"csadvent-2019-{slot.Day}-{slot.Part}");
        Assert.StartsWith($"Wombats tour the 2019 archive. Day {slot.Day}, post {slot.Part} of C# Advent 2019, by ", item.Element("description")!.Value);
    }

    private async Task<int> GiveSpotAsync(long githubUserId, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var contributorId = await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = "Sue Summary";
        slot.ContributorId = contributorId;
        await db.SaveChangesAsync(CancellationToken);
        return slot.Id;
    }
}
