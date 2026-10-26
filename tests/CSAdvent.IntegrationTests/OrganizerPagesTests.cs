using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The organizer pages on a phone and what needs attention (#169, #170, #173): seasons 2540-2545, and GitHub users from 254001.
/// </summary>
public sealed partial class OrganizerPagesTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EntriesShowWhatNeedsAttention()
    {
        await TestSeasons.EnsureAsync(fixture, 2540, claimsOpen: true, season =>
        {
            var ready = Spot(season, 1);
            ready.AuthorName = "Ada Ready";
            ready.PostTitle = "A finished post";
            ready.PostUrl = "https://example.com/ada-2540";
            ready.Contact = new SlotContact { Email = "ada-2540@example.com" };
            Spot(season, 2).AuthorName = "Bob Nolink";
            Spot(season, 3).DroppedOutAt = DateTimeOffset.UtcNow;
            Spot(season, 4).AuthorName = "Cy Urlonly";
            Spot(season, 4).PostUrl = "https://example.com/cy-2540";
        });
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var all = WebUtility.HtmlDecode(await organizer.GetPageAsync("/manage/entries?year=2540"));
        Assert.Contains("href=\"mailto:ada-2540@example.com\"", all);
        Assert.Contains("<a href=\"https://example.com/ada-2540\" target=\"_blank\"", all);
        Assert.Contains("(dropped out)", all);
        Assert.Matches(@"Needs attention <span class=""ml-1 tabular-nums"">\(3\)</span>", all);
        // The table scrolls sideways on a phone instead of running off the screen.
        Assert.Contains("overflow-x-auto", all);

        var attention = WebUtility.HtmlDecode(await organizer.GetPageAsync("/manage/entries?year=2540&show=attention"));
        Assert.Contains("Bob Nolink", attention);
        Assert.Contains("Cy Urlonly", attention);
        Assert.Contains("December 3 · Post 1", attention);
        Assert.DoesNotContain("Ada Ready", attention);
        Assert.Contains("href=\"manage/entries?year=2540\"", attention);
    }

    [Fact]
    public async Task TheEntryEditorShowsWhoWasAskedToStepIn()
    {
        var now = DateTimeOffset.UtcNow;
        await TestSeasons.EnsureAsync(fixture, 2541, claimsOpen: true, season =>
        {
            Spot(season, 5).AuthorName = "Dan Late";
            season.Substitutes.Add(new Substitute { Name = "Eve Backup", Email = "eve-2541@example.com", SignedUpAt = now.AddDays(-3) });
            season.Substitutes.Add(new Substitute { Name = "Fay Backup", Email = "fay-2541@example.com", SignedUpAt = now.AddDays(-2) });
        });
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == 2541 && s.Day == 5 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
            var backups = await db.Substitutes.Where(s => s.SeasonYear == 2541).OrderBy(s => s.SignedUpAt).ToListAsync(CancellationToken);
            if (!await db.BackupOffers.AnyAsync(o => o.SlotId == slotId, CancellationToken))
            {
                db.BackupOffers.Add(new BackupOffer { SlotId = slotId, SubstituteId = backups[0].Id, AskedAt = now.AddHours(-6), ExpiresAt = now.AddHours(-2), Status = BackupOfferStatus.Declined, RespondedAt = now.AddHours(-5) });
                db.BackupOffers.Add(new BackupOffer { SlotId = slotId, SubstituteId = backups[1].Id, AskedAt = now.AddHours(-2), ExpiresAt = now.AddHours(2), Status = BackupOfferStatus.Asked });
                await db.SaveChangesAsync(CancellationToken);
            }
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = WebUtility.HtmlDecode(await organizer.GetPageAsync($"/manage/entries/{slotId}"));

        Assert.Contains("Backup posters asked", page);
        Assert.True(page.IndexOf("Eve Backup", StringComparison.Ordinal) < page.IndexOf("Fay Backup", StringComparison.Ordinal), "Backups are listed in the order they were asked.");
        Assert.Contains("href=\"mailto:fay-2541@example.com\"", page);
        Assert.Contains("They said not this time.", page);
        Assert.Contains("No answer yet.", page);
    }

    [Fact]
    public async Task ButtonsComeBackToTheItemTheyWereOn()
    {
        await TestSeasons.EnsureAsync(fixture, 2542, claimsOpen: true, season =>
        {
            var slot = Spot(season, 6);
            slot.AuthorName = "Gus Social";
            slot.PostTitle = "A 2542 post";
            slot.PostUrl = "https://example.com/gus-2542";
        });
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == 2542 && s.Day == 6 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // Each announcement comes back to itself, with the result in the query string ahead of the fragment.
        const string Page = "/manage/social?year=2542&day=6";
        var html = WebUtility.HtmlDecode(await organizer.GetPageAsync(Page));
        Assert.Contains($"id=\"announcement-{slotId}-Bluesky-card\"", html);
        Assert.Contains($"value=\"manage/social?year=2542&day=6#announcement-{slotId}-Bluesky-card\"", html);
        Assert.Contains("aria-label=\"Link to the Bluesky post (optional)\"", html);
        Assert.True(html.IndexOf("A 2542 post", StringComparison.Ordinal) < html.IndexOf("How each platform is posted</h2>", StringComparison.Ordinal), "The day's posts come before the platforms.");

        var marked = await PostAsync(organizer, Page, "/manage/social/mark", new()
        {
            ["SlotId"] = $"{slotId}",
            ["Platform"] = nameof(SocialPlatform.Bluesky),
            ["ReturnUrl"] = $"manage/social?year=2542&day=6#announcement-{slotId}-Bluesky-card",
        });
        Assert.Equal($"/manage/social?year=2542&day=6&error=not-revealed#announcement-{slotId}-Bluesky-card", marked.Headers.Location!.OriginalString);

        // The blurb buttons on the entry editor come back to the blurb panel.
        var decided = await PostAsync(organizer, $"/manage/entries/{slotId}", "/manage/blurbs/decide", new()
        {
            ["SlotId"] = $"{slotId}",
            ["Decision"] = "Passed",
            ["ReturnUrl"] = $"manage/entries/{slotId}#blurb",
        });
        Assert.Equal($"/manage/entries/{slotId}?blurb=decided#blurb", decided.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheNavigationReachesEveryPageAndCountsWhatsWaiting()
    {
        using var claimant = await fixture.SignInAsync("nav-claimant-2543", 254001);
        int requestId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributor = await db.Contributors.SingleAsync(c => c.GithubUserId == 254001, CancellationToken);
            var post = await db.Slots.SingleAsync(s => s.SeasonYear == 2018 && s.Day == 25 && s.Part == 2, CancellationToken);
            var request = new PostClaimRequest { SlotId = post.Id, ContributorId = contributor.Id, SubmittedAt = DateTimeOffset.UtcNow, Status = PostClaimStatus.Pending };
            db.PostClaimRequests.Add(request);
            await db.SaveChangesAsync(CancellationToken);
            requestId = request.Id;
        }

        try
        {
            using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
            var page = await organizer.GetPageAsync("/manage/authors/requests");

            var nav = OrganizerNav().Match(page).Value;
            foreach (var href in new[] { "manage/authors/link-changes", "manage/social/signups", "manage/emails/queue" })
            {
                Assert.Contains($"href=\"{href}\"", nav);
            }

            // A post claim's page is Post claims, not Authors, and its count is on it.
            Assert.Matches(@"href=""manage/authors/requests"" aria-current=""page""[^>]*>\s*Post claims\s*<span[^>]*><span class=""sr-only""> \(</span>[1-9]\d*<span class=""sr-only""> waiting\)</span>", nav);
            Assert.DoesNotMatch(@"href=""manage/authors"" aria-current", nav);
        }
        finally
        {
            await using var db = await fixture.CreateDbContextAsync();
            await db.PostClaimRequests.Where(r => r.Id == requestId).ExecuteDeleteAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task FormErrorsAreAnnouncedAndShownWithTheirField()
    {
        await TestSeasons.EnsureAsync(fixture, 2543, claimsOpen: true);
        using var browser = await fixture.SignInAsync("form-errors-2543", 254002);

        var response = await browser.PostFormAsync("/claim/2543/2/1", "claim", new()
        {
            ["Form.DisplayName"] = "Hal Errors",
            ["Form.Link"] = "not a web address",
            ["Form.AgreeToTiming"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(CancellationToken));
        // The summary is announced: it's in an alert, which is only hidden while it has nothing in it.
        Assert.Matches(@"<div role=""alert"" class=""\[&:not\(:has\(li\)\)\]:hidden""><ul class=""rounded-xl[^""]*""[^>]*><li", page);
        var link = Assert.Single(InputTag().Matches(page), tag => tag.Value.Contains("id=\"link\"", StringComparison.Ordinal)).Value;
        Assert.Contains("aria-describedby=\"link-help link-error\"", link);
        Assert.Contains("aria-invalid=\"true\"", link);
        Assert.Matches(@"<div id=""link-error""><div class=""validation-message""[^>]*>Enter a full web address that starts with https:// or http://\.</div></div>", page);
        Assert.Matches(@"<div id=""terms-error"" class=""ml-7""><div class=""validation-message""[^>]*>Agree to the terms to continue\.</div></div>", page);
        // A field without an error has nothing to say: Blazor's placeholder for it is hidden.
        Assert.Matches(@"<div id=""display-name-error""><div class=""validation-message""[^>]*hidden[^>]*></div></div>", page);
    }

    private static Slot Spot(Season season, int day) => season.Slots.Single(s => s.Day == day && s.Part == 1);

    /// <summary>Posts one of a page's plain forms to a minimal API, with the page's antiforgery token.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string page, string action, Dictionary<string, string> fields)
    {
        var html = await browser.GetPageAsync(page);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value);
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex("<nav aria-label=\"Organizer\".*?</nav>", RegexOptions.Singleline)]
    private static partial Regex OrganizerNav();

    [GeneratedRegex("<input [^>]*>")]
    private static partial Regex InputTag();
}
