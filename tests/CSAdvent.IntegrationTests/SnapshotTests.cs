using System.Globalization;
using System.Net.Http.Json;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The Wayback Machine snapshots (#261), on the stand-in archive (<see cref="CSAdvent.Web.Snapshots.SampleSnapshotSaver"/>), so
/// nothing reaches the Internet Archive. Runs go through the development endpoint at chosen times, because the AppHost turns
/// the worker off in Testing. December days are revealed at 05:00 UTC, and a season ends at 05:00 UTC on December 26.
/// Seasons 2690-2699.
/// </summary>
public sealed class SnapshotTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachPostIsSavedOnItsDayOnceItsUpAndAgainAfterTheSeason()
    {
        const int year = 2690;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Ada", "https://ada.links.csadvent.test/working-2690");
            Post(season, 1, 2, "Bob", "https://bob.links.csadvent.test/working-2690");
            Post(season, 3, 1, "Cy", "https://cy.links.csadvent.test/working-2690");
        });

        // The blurb check has seen Ada's post, and Cy's, which went up before its day.
        await PassBlurbAsync(await SlotIdAsync(year, 1, 1));
        await PassBlurbAsync(await SlotIdAsync(year, 3, 1));

        // At 10 AM Eastern on December 1, only Ada's: Bob's may not be up yet, and Cy's isn't revealed. It's saved by the next run.
        var december1 = new DateTimeOffset(2690, 12, 1, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new Run(1, 0, 0), await RunAsync(year, december1));
        Assert.Equal(new Run(0, 1, 0), await RunAsync(year, december1.AddMinutes(15)));
        Assert.Equal(new Run(0, 0, 0), await RunAsync(year, december1.AddHours(3)));

        // Bob's once December 1 is over, whatever the blurb check said.
        var december2 = new DateTimeOffset(2690, 12, 2, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(new Run(1, 0, 0), await RunAsync(year, december2));
        Assert.Equal(new Run(0, 1, 0), await RunAsync(year, december2.AddMinutes(15)));

        // After the season, every post again, Cy's too, whose day went by without a run (so it never got one on its day).
        var december26 = new DateTimeOffset(2690, 12, 26, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(new Run(3, 0, 0), await RunAsync(year, december26));
        Assert.Equal(new Run(0, 3, 0), await RunAsync(year, december26.AddMinutes(15)));
        Assert.Equal(new Run(0, 0, 0), await RunAsync(year, december26.AddDays(30)));

        Assert.Equal(
            [
                (1, 1, SnapshotMoment.Live, SnapshotStatus.Saved, "https://web.archive.org/web/26901201150000/https://ada.links.csadvent.test/working-2690"),
                (1, 1, SnapshotMoment.AfterSeason, SnapshotStatus.Saved, "https://web.archive.org/web/26901226050000/https://ada.links.csadvent.test/working-2690"),
                (1, 2, SnapshotMoment.Live, SnapshotStatus.Saved, "https://web.archive.org/web/26901202050000/https://bob.links.csadvent.test/working-2690"),
                (1, 2, SnapshotMoment.AfterSeason, SnapshotStatus.Saved, "https://web.archive.org/web/26901226050000/https://bob.links.csadvent.test/working-2690"),
                (3, 1, SnapshotMoment.AfterSeason, SnapshotStatus.Saved, "https://web.archive.org/web/26901226050000/https://cy.links.csadvent.test/working-2690"),
            ],
            (await SnapshotsAsync(year)).Select(s => (s.Slot.Day, s.Slot.Part, s.Moment, s.Status, s.SnapshotUrl)));

        // The organizers see a post's snapshots on its entry page.
        await using var db = await fixture.CreateDbContextAsync();
        var rows = await new ServicesOnAClock(db, december26).Snapshots().ListAsync(await SlotIdAsync(year, 1, 1), CancellationToken);
        Assert.Equal([SnapshotMoment.Live, SnapshotMoment.AfterSeason], rows.Select(r => r.Moment));
    }

    [Fact]
    public async Task PastSeasonsPostsAreSavedUnlessTheyreGoneAndANewAddressIsSavedToo()
    {
        const int year = 2691;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Ann", "https://ann.links.csadvent.test/working-2691");
            Post(season, 1, 2, "Ben", "https://ben.links.csadvent.test/gone-2691");
            Post(season, 2, 1, "Cal", "https://cal.links.csadvent.test/down-2691");
            Post(season, 2, 2, "Dot", "https://dot.links.csadvent.test/working-2691");
        });
        var (ann, ben, cal) = (await SlotIdAsync(year, 1, 1), await SlotIdAsync(year, 1, 2), await SlotIdAsync(year, 2, 1));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var checkedAt = new DateTimeOffset(2692, 2, 1, 12, 0, 0, TimeSpan.Zero);
            db.LinkChecks.AddRange(
                new LinkCheck { SlotId = ann, PostUrl = "https://ann.links.csadvent.test/working-2691", Status = LinkStatus.Working, NextCheckAt = checkedAt.AddDays(30) },
                new LinkCheck { SlotId = ben, PostUrl = "https://ben.links.csadvent.test/gone-2691", Status = LinkStatus.Dead, NextCheckAt = checkedAt.AddDays(30) },
                new LinkCheck { SlotId = cal, PostUrl = "https://cal.links.csadvent.test/down-2691", Status = LinkStatus.Failing, NextCheckAt = checkedAt.AddDays(1) });
            await db.SaveChangesAsync(CancellationToken);
        }

        // Months later, the posts still up (and Dot's, not checked yet), but not Ben's, which is gone, or Cal's, which is failing.
        var march = new DateTimeOffset(2692, 3, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new Run(2, 0, 0), await RunAsync(year, march));
        Assert.Equal(new Run(0, 2, 0), await RunAsync(year, march.AddMinutes(15)));

        // Cal's loads again, and Ann's moved: both are saved, and Ann's old address keeps its snapshot.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            await db.LinkChecks.Where(c => c.SlotId == cal).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, LinkStatus.Working), CancellationToken);
            await db.Slots.Where(s => s.Id == ann).ExecuteUpdateAsync(s => s.SetProperty(x => x.PostUrl, "https://ann.links.csadvent.test/working-2691-moved"), CancellationToken);
        }

        Assert.Equal(new Run(2, 0, 0), await RunAsync(year, march.AddHours(1)));
        Assert.Equal(new Run(0, 2, 0), await RunAsync(year, march.AddHours(1).AddMinutes(15)));

        Assert.Equal(
            [
                ("Ann", "https://ann.links.csadvent.test/working-2691", SnapshotStatus.Saved),
                ("Ann", "https://ann.links.csadvent.test/working-2691-moved", SnapshotStatus.Saved),
                ("Cal", "https://cal.links.csadvent.test/down-2691", SnapshotStatus.Saved),
                ("Dot", "https://dot.links.csadvent.test/working-2691", SnapshotStatus.Saved),
            ],
            (await SnapshotsAsync(year)).Select(s => (s.Slot.AuthorName!, s.PostUrl, s.Status)).Order());
        Assert.All(await SnapshotsAsync(year), s => Assert.Equal(SnapshotMoment.AfterSeason, s.Moment));
    }

    [Fact]
    public async Task ASnapshotTheArchiveCantSaveIsTriedAgainLaterThenGivenUpOn()
    {
        const int year = 2692;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Gil", "https://gil.links.csadvent.test/gone-2692");
            Post(season, 1, 2, "Hap", "https://hap.links.csadvent.test/busy-2692");
            Post(season, 2, 1, "Ike", "https://ike.links.csadvent.test/slow-2692");
        });

        // Gil's and Ike's are started; the archive doesn't answer about Hap's.
        var start = new DateTimeOffset(2693, 1, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new Run(2, 0, 0), await RunAsync(year, start));

        // Gil's can't be saved, so it's a try. Ike's is still going.
        await RunAsync(year, start.AddMinutes(15));
        var gil = await SnapshotAsync(year, "Gil");
        Assert.Equal((SnapshotStatus.Due, 1, start.AddMinutes(75)), (gil.Status, gil.Attempts, gil.NextAttemptAt));
        Assert.Contains("(error:not-found)", gil.Detail);
        Assert.Equal(SnapshotStatus.Capturing, (await SnapshotAsync(year, "Ike")).Status);

        // An hour on, Ike's never said it was done, which is a try too. Hap's is asked for again, and still isn't answered,
        // which isn't the post's fault, so it isn't a try.
        await RunAsync(year, start.AddMinutes(60));
        var ike = await SnapshotAsync(year, "Ike");
        Assert.Equal((SnapshotStatus.Due, 1, "The Wayback Machine didn't say it had saved it within an hour."), (ike.Status, ike.Attempts, ike.Detail));
        var hap = await SnapshotAsync(year, "Hap");
        Assert.Equal((SnapshotStatus.Due, 0, start.AddMinutes(120), "The Wayback Machine answered 503."), (hap.Status, hap.Attempts, hap.NextAttemptAt, hap.Detail));

        // Gil's is tried at longer and longer intervals, and given up on at the fifth try.
        for (var tries = 0; tries < 10 && gil.Status != SnapshotStatus.Failed; tries++)
        {
            await RunAsync(year, gil.NextAttemptAt);
            await RunAsync(year, gil.NextAttemptAt.AddMinutes(15));
            gil = await SnapshotAsync(year, "Gil");
        }

        Assert.Equal((SnapshotStatus.Failed, 5), (gil.Status, gil.Attempts));
        await RunAsync(year, start.AddDays(30));
        Assert.Equal((SnapshotStatus.Failed, 5), ((await SnapshotAsync(year, "Gil")).Status, (await SnapshotAsync(year, "Gil")).Attempts));
    }

    [Fact]
    public async Task ReadersOfAPostMarkedGoneGetOurEarliestSnapshotOfIt()
    {
        const int year = 2693;
        const string url = "https://jo.links.csadvent.test/gone-archived-2693";
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season => Post(season, 3, 1, "Jo", url));
        var slotId = await SlotIdAsync(year, 3, 1);

        // Saved on its day and after the season, and once at an address it had before, earlier still.
        const string onItsDay = $"https://web.archive.org/web/26931203150000/{url}";
        await AddSavedAsync(slotId, SnapshotMoment.AfterSeason, url, $"https://web.archive.org/web/26931226060000/{url}", new DateTimeOffset(2693, 12, 26, 6, 0, 0, TimeSpan.Zero));
        await AddSavedAsync(slotId, SnapshotMoment.Live, url, onItsDay, new DateTimeOffset(2693, 12, 3, 15, 0, 0, TimeSpan.Zero));
        await AddSavedAsync(slotId, SnapshotMoment.Live, "https://jo.links.csadvent.test/old-2693", "https://web.archive.org/web/26931202000000/https://jo.links.csadvent.test/old-2693", new DateTimeOffset(2693, 12, 2, 0, 0, 0, TimeSpan.Zero));

        // "It's gone": the stand-in archive has a copy of its own (closest to the reveal), but ours wins.
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await DeadLinkTests.PostAsync(organizer, "dead", slotId);
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(onItsDay, await db.LinkChecks.Where(c => c.SlotId == slotId).Select(c => c.ArchivedUrl).SingleAsync(CancellationToken));

        var entries = new ServicesOnAClock(db, new DateTimeOffset(2694, 1, 10, 12, 0, 0, TimeSpan.Zero)).Entries();
        var post = (await entries.FindPostAsync(year, 3, 1, CancellationToken))!.Value;
        Assert.Equal(onItsDay, (await entries.DestinationAsync(post.SlotId, post.PostUrl, CancellationToken)).Url);
    }

    [Fact]
    public async Task ThePostsTheDeadLinkCheckFindsGoneGetOurSnapshot()
    {
        const int year = 2694;
        const string url = "https://kim.links.csadvent.test/gone-2694";
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season => Post(season, 4, 1, "Kim", url));
        const string saved = $"https://web.archive.org/web/26941204150000/{url}";
        await AddSavedAsync(await SlotIdAsync(year, 4, 1), SnapshotMoment.Live, url, saved, new DateTimeOffset(2694, 12, 4, 15, 0, 0, TimeSpan.Zero));

        // Three failed checks, days apart, and it's dead. The stand-in archive has no copy of it, but we do.
        var start = new DateTimeOffset(2695, 1, 5, 12, 0, 0, TimeSpan.Zero);
        await DeadLinkTests.RunAsync(fixture, year, start);
        await DeadLinkTests.RunAsync(fixture, year, start.AddDays(1).AddHours(1));
        Assert.Equal(new DeadLinkTests.LinkRun(1, 1, 1), await DeadLinkTests.RunAsync(fixture, year, start.AddDays(3).AddHours(1)));

        await using var db = await fixture.CreateDbContextAsync();
        var check = await db.LinkChecks.Include(c => c.Slot).SingleAsync(c => c.Slot.SeasonYear == year, CancellationToken);
        Assert.Equal((LinkStatus.Dead, saved), (check.Status, check.ArchivedUrl));
    }

    private static Slot Post(Season season, int day, int part, string author, string url)
    {
        var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
        slot.AuthorName = author;
        slot.PostTitle = $"{author}'s post";
        slot.PostUrl = url;
        return slot;
    }

    private async Task PassBlurbAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var url = await db.Slots.Where(s => s.Id == slotId).Select(s => s.PostUrl!).SingleAsync(CancellationToken);
        db.BlurbChecks.Add(new BlurbCheck { SlotId = slotId, PostUrl = url, Status = BlurbStatus.Passed, StatusSince = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task AddSavedAsync(int slotId, SnapshotMoment moment, string postUrl, string snapshotUrl, DateTimeOffset savedAt)
    {
        await using var db = await fixture.CreateDbContextAsync();
        db.PostSnapshots.Add(new PostSnapshot
        {
            SlotId = slotId,
            Moment = moment,
            PostUrl = postUrl,
            Status = SnapshotStatus.Saved,
            NextAttemptAt = savedAt,
            RequestedAt = savedAt,
            SnapshotUrl = snapshotUrl,
            SavedAt = savedAt,
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<Run> RunAsync(int year, DateTimeOffset at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/snapshots/run?year={year}&at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Run>(CancellationToken))!;
    }

    private async Task<List<PostSnapshot>> SnapshotsAsync(int year)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.PostSnapshots
            .AsNoTracking()
            .Include(s => s.Slot)
            .Where(s => s.Slot.SeasonYear == year)
            .OrderBy(s => s.Slot.Day).ThenBy(s => s.Slot.Part).ThenBy(s => s.Moment == SnapshotMoment.AfterSeason)
            .ToListAsync(CancellationToken);
    }

    private async Task<PostSnapshot> SnapshotAsync(int year, string author)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.PostSnapshots.AsNoTracking().SingleAsync(s => s.Slot.SeasonYear == year && s.Slot.AuthorName == author, CancellationToken);
    }

    private async Task<int> SlotIdAsync(int year, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private sealed record Run(int Asked, int Saved, int GaveUp);
}
