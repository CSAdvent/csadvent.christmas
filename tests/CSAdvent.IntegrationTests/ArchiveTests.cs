using System.Net;
using System.Text;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The 2017-2025 history is seeded by the AppHost's migrations step, before the web app starts, and each finished season can
/// be exported to join it.
/// </summary>
public sealed class ArchiveTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryArchivedSeasonIsSeededAfterMigrations()
    {
        await using var db = await fixture.CreateDbContextAsync();

        var posts = await db.Slots
            .Where(s => s.SeasonYear >= 2017 && s.SeasonYear <= 2025)
            .GroupBy(s => s.SeasonYear)
            .Select(g => new { Year = g.Key, Slots = g.Count(), Posts = g.Count(s => s.PostUrl != null) })
            .OrderBy(g => g.Year)
            .ToListAsync(CancellationToken);

        Assert.Equal(Enumerable.Range(2017, 9), posts.Select(p => p.Year));
        Assert.Equal(25, posts[0].Slots);
        Assert.All(posts.Skip(1), p => Assert.Equal(50, p.Slots));
        Assert.Equal(408, posts.Sum(p => p.Posts));
    }

    [Fact]
    public async Task SeedingAgainAddsNothing()
    {
        await using var db = await fixture.CreateDbContextAsync();
        var before = await db.Slots.CountAsync(s => s.SeasonYear <= 2025, CancellationToken);

        ArchiveSeeder.Seed(db);
        await ArchiveSeeder.SeedAsync(db, CancellationToken);

        Assert.Equal(before, await db.Slots.CountAsync(s => s.SeasonYear <= 2025, CancellationToken));
    }

    [Fact]
    public async Task OrganizersDownloadEachFinishedSeasonForTheArchiveFiles()
    {
        await TestSeasons.EnsureAsync(fixture, 2500, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 6, Part: 2 });
            slot.AuthorName = "Export Ezra";
            slot.AuthorUrl = "https://ezra.example.com";
            slot.PostTitle = "Exporting 2500";
            slot.PostUrl = "https://ezra.example.com/exporting";
            slot.PostSummary = "How a season reaches the archive files.";
            slot.Contact = new SlotContact { Email = "ezra-export@example.com" };
        });
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // Not before the season is over, and never the test season.
        Assert.Equal(HttpStatusCode.NotFound, (await organizer.GetAsync("/manage/seasons/2500/archive.json", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await organizer.GetAsync($"/manage/seasons/{Season.TestYear}/archive.json", CancellationToken)).StatusCode);

        // Once its last day is over, it's the calendar as shown, summaries included, without contact details.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2500, 12, 26, 12, 0, 0, TimeSpan.Zero));
            var schedule = new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), clock);
            var exported = await new SeasonArchiveExport(db, schedule).GetAsync(2500, CancellationToken);

            Assert.Equal(50, exported!.Slots.Count);
            var json = Encoding.UTF8.GetString(ArchiveSeeder.Serialize(exported));
            Assert.Contains("\"postSummary\": \"How a season reaches the archive files.\"", json);
            Assert.DoesNotContain("ezra-export@example.com", json);
        }

        // The seasons page links each finished season's file, named for Data/Archive.
        Assert.Contains("href=\"manage/seasons/2019/archive.json\"", await organizer.GetStringAsync("/manage/seasons", CancellationToken));
        var download = await organizer.GetAsync("/manage/seasons/2019/archive.json", CancellationToken);
        download.EnsureSuccessStatusCode();
        Assert.Equal("2019.json", download.Content.Headers.ContentDisposition?.FileNameStar);
        var season = ArchiveSeeder.Deserialize(await download.Content.ReadAsStreamAsync(CancellationToken))!;
        Assert.Equal((2019, 2, 50), (season.Year, season.SlotsPerDay, season.Slots.Count));

        using var contributor = await fixture.SignInAsync("archive-nosy", 25001);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf((await contributor.GetAsync("/manage/seasons/2019/archive.json", CancellationToken)).Headers.Location!));
    }
}
