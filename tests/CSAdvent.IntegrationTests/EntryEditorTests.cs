using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The organizers' entry editor. Season 2104 starts with December 1, part 1 claimed (title only), December 3, part 1
/// claimed with a contact email, and December 7, part 2 claimed (title only) for the list test alone; every other spot
/// is open. Each test changes its own spots, so the order they run in doesn't matter.
/// </summary>
public sealed class EntryEditorTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/manage/entries")]
    [InlineData("/manage/entries/1")]
    public async Task ContributorsCannotOpenTheEditor(string path)
    {
        using var browser = await fixture.SignInAsync("not-an-editor", 7201);

        var response = await browser.GetAsync(path, CancellationToken);

        Assert.StartsWith("/access-denied", TestBrowser.PathOf(response.Headers.Location!));
    }

    [Fact]
    public async Task TheListShowsHowCompleteEachSpotIs()
    {
        await EnsureSeasonAsync();
        using var organizer = await SignInAsOrganizerAsync();

        var html = await organizer.GetStringAsync("/manage/entries?year=2104", CancellationToken);

        Assert.Contains("Listed Author", html);
        Assert.Contains(">Title only</span>", html);
        Assert.Contains(">Open</span>", html);
    }

    [Fact]
    public async Task OrganizersPutSubstitutesIntoOpenSpots()
    {
        await EnsureSeasonAsync();
        var slotId = await SlotIdAsync(2, 1);
        using var organizer = await SignInAsOrganizerAsync();

        var response = await organizer.PostFormAsync($"/manage/entries/{slotId}", "entry", new()
        {
            ["Form.AuthorName"] = "Sam Substitute",
            ["Form.AuthorUrl"] = "https://example.com/sam",
            ["Form.ContactEmail"] = "sam@example.com",
        });

        Assert.Equal($"/manage/entries/{slotId}?done=saved", TestBrowser.PathOf(response.Headers.Location!));
        var slot = await LoadAsync(slotId);
        Assert.Equal(("Sam Substitute", "https://example.com/sam", "sam@example.com"), (slot.AuthorName, slot.AuthorUrl, slot.Contact?.Email));
        Assert.Null(slot.ContributorId);
    }

    [Fact]
    public async Task OrganizersCompleteAPost()
    {
        await EnsureSeasonAsync();
        var slotId = await SlotIdAsync(1, 1);
        using var organizer = await SignInAsOrganizerAsync();

        await organizer.PostFormAsync($"/manage/entries/{slotId}", "entry", new()
        {
            ["Form.AuthorName"] = "Claimed Author",
            ["Form.PostTitle"] = "Finished Title",
            ["Form.PostUrl"] = "https://example.com/finished",
        });

        var slot = await LoadAsync(slotId);
        Assert.Equal(("Finished Title", "https://example.com/finished"), (slot.PostTitle, slot.PostUrl));
    }

    [Fact]
    public async Task ReleasingClearsTheSpotAndItsContact()
    {
        await EnsureSeasonAsync();
        var slotId = await SlotIdAsync(3, 1);
        using var organizer = await SignInAsOrganizerAsync();

        var unconfirmed = await organizer.PostFormAsync($"/manage/entries/{slotId}", "release", []);
        Assert.Contains("Tick the box to confirm", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));
        Assert.NotNull((await LoadAsync(slotId)).AuthorName);

        var released = await organizer.PostFormAsync($"/manage/entries/{slotId}", "release", new() { ["Release.Confirm"] = "true" });

        Assert.Equal($"/manage/entries/{slotId}?done=released", TestBrowser.PathOf(released.Headers.Location!));
        var slot = await LoadAsync(slotId);
        Assert.Equal((null, null, null, null), (slot.AuthorName, slot.AuthorUrl, slot.PostTitle, slot.PostUrl));
        Assert.Null(slot.Contact);
    }

    [Fact]
    public async Task AnEditMadeOnAnOutdatedVersionIsRefused()
    {
        await EnsureSeasonAsync();
        var slotId = await SlotIdAsync(4, 1);
        using var organizer = await SignInAsOrganizerAsync();
        var staleVersion = TestBrowser.HiddenValue(await organizer.GetStringAsync($"/manage/entries/{slotId}", CancellationToken), "Form.Version");

        // Someone else saves the spot after the form was opened.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).AuthorName = "Changed Elsewhere";
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await organizer.PostFormAsync($"/manage/entries/{slotId}", "entry", new()
        {
            ["Form.AuthorName"] = "Overwriting Edit",
            ["Form.Version"] = staleVersion,
        });

        Assert.Contains("Someone changed this spot since you opened it", await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("Changed Elsewhere", (await LoadAsync(slotId)).AuthorName);
    }

    [Fact]
    public async Task APostWithoutAnAuthorIsRefused()
    {
        await EnsureSeasonAsync();
        var slotId = await SlotIdAsync(5, 2);
        using var organizer = await SignInAsOrganizerAsync();

        var response = await organizer.PostFormAsync($"/manage/entries/{slotId}", "entry", new() { ["Form.PostTitle"] = "Orphan Post" });

        Assert.Contains("A post needs an author.", (await response.Content.ReadAsStringAsync(CancellationToken)).Replace("&#x27;", "'"));
        Assert.Null((await LoadAsync(slotId)).PostTitle);
    }

    private Task<HttpClient> SignInAsOrganizerAsync() => fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

    private Task EnsureSeasonAsync() => TestSeasons.EnsureAsync(fixture, 2104, claimsOpen: false, season =>
    {
        var claimed = season.Slots.Single(s => s is { Day: 1, Part: 1 });
        claimed.AuthorName = "Claimed Author";
        claimed.PostTitle = "Working Title";

        var listed = season.Slots.Single(s => s is { Day: 7, Part: 2 });
        listed.AuthorName = "Listed Author";
        listed.PostTitle = "Listed Title";

        var withContact = season.Slots.Single(s => s is { Day: 3, Part: 1 });
        withContact.AuthorName = "Dropping Out";
        withContact.AuthorUrl = "https://example.com/dropping";
        withContact.Contact = new SlotContact { Email = "dropping@example.com" };
    });

    private async Task<int> SlotIdAsync(int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == 2104 && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private async Task<Slot> LoadAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.AsNoTracking().Include(s => s.Contact).SingleAsync(s => s.Id == slotId, CancellationToken);
    }
}
