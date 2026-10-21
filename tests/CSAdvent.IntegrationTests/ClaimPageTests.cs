using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The claim form, posted the way a browser does. Season 2097 has claims open, with December 3, part 1 already claimed;
/// season 2098 hasn't opened claims. Both are later than the 2089 season the home page tests rely on.
/// </summary>
public sealed partial class ClaimPageTests(AppHostFixture fixture)
{
    private static readonly SemaphoreSlim SeasonsCreated = new(1, 1);
    private static bool s_seasonsExist;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnonymousVisitorsAreAskedToSignIn()
    {
        await EnsureSeasonsAsync();
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/claim/2097/2/1", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?ReturnUrl=%2Fclaim%2F2097%2F2%2F1", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task TheFormShowsTheSpotAndTheVerifiedEmail()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-viewer", 6001);

        var html = await browser.GetStringAsync("/claim/2097/2/1", CancellationToken);

        Assert.Contains(">Request December 2, 2097</h1>", html);
        Assert.Contains("claim-viewer@example.com", html);
        Assert.Contains("value=\"claim-viewer\"", html);
    }

    [Fact]
    public async Task SubmittingStoresAPendingRequest()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-ada", 6002);

        var response = await PostClaimAsync(browser, "/claim/2097/4/2", new()
        {
            ["Form.DisplayName"] = "Ada Lovelace",
            ["Form.Link"] = "https://example.com/ada",
            ["Form.Comments"] = "December 5 also works",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/claim/requested/2097/4/2", PathOf(response.Headers.Location!));

        await using var db = await fixture.CreateDbContextAsync();
        var request = await db.ClaimRequests
            .Include(r => r.Slot)
            .Include(r => r.Contributor)
            .SingleAsync(r => r.Contributor.GithubUserId == 6002, CancellationToken);
        Assert.Equal((2097, 4, 2), (request.Slot.SeasonYear, request.Slot.Day, request.Slot.Part));
        Assert.Equal(ClaimRequestStatus.Pending, request.Status);
        Assert.Equal(("Ada Lovelace", "claim-ada@example.com", "https://example.com/ada", "December 5 also works"),
            (request.DisplayName, request.Email, request.Link, request.Comments));
        // Requesting doesn't claim the slot; the organizer approves one request.
        Assert.Null(request.Slot.AuthorName);
    }

    [Fact]
    public async Task RequestingTheSameSpotTwiceIsRefused()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-twice", 6003);
        var form = new Dictionary<string, string>
        {
            ["Form.DisplayName"] = "Twice",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        };

        await PostClaimAsync(browser, "/claim/2097/5/1", form);
        var second = await PostClaimAsync(browser, "/claim/2097/5/1", form);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("You&#x27;ve already requested this spot", await second.Content.ReadAsStringAsync(CancellationToken));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(1, await db.ClaimRequests.CountAsync(r => r.Contributor.GithubUserId == 6003, CancellationToken));
    }

    [Fact]
    public async Task BothAgreementsAreRequiredAndTheFormKeepsWhatWasEntered()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-forgetful", 6004);

        var response = await PostClaimAsync(browser, "/claim/2097/6/1", new()
        {
            ["Form.DisplayName"] = "Forgetful",
            ["Form.AgreeToTerms"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("Agree to publish your post on your day to continue.", html);
        Assert.Matches("name=\"Form\\.AgreeToTerms\"[^>]*checked|checked[^>]*name=\"Form\\.AgreeToTerms\"", html);
        Assert.Contains("value=\"Forgetful\"", html);
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.ClaimRequests.AnyAsync(r => r.Contributor.GithubUserId == 6004, CancellationToken));
    }

    [Theory]
    [InlineData("/claim/2097/3/1", "This spot has already been claimed.")]
    [InlineData("/claim/2098/1/1", "Sign-ups aren&#x27;t open for this season.")]
    public async Task UnavailableSpotsExplainWhy(string path, string message)
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-late", 6005);

        var html = await browser.GetStringAsync(path, CancellationToken);

        Assert.Contains(message, html);
        Assert.DoesNotContain("Request this spot", html);
    }

    [Theory]
    [InlineData("/claim/2097/26/1")]
    [InlineData("/claim/1999/1/1")]
    public async Task UnknownSpotsReturnNotFound(string path)
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-lost", 6006);

        var response = await browser.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> SignInAsync(string login, long githubUserId)
    {
        var browser = fixture.CreateBrowser();
        var response = await browser.GetAsync($"/dev/signin?login={login}&id={githubUserId}", CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>Loads the form for its antiforgery token and handler name, then posts it with the given fields.</summary>
    private static async Task<HttpResponseMessage> PostClaimAsync(HttpClient browser, string path, Dictionary<string, string> fields)
    {
        var page = await browser.GetStringAsync(path, CancellationToken);
        fields["__RequestVerificationToken"] = HiddenInput("__RequestVerificationToken").Match(page).Groups["value"].Value;
        fields["_handler"] = HiddenInput("_handler").Match(page).Groups["value"].Value;

        return await browser.PostAsync(path, new FormUrlEncodedContent(fields), CancellationToken);
    }

    private static string PathOf(Uri location) => location.IsAbsoluteUri ? location.PathAndQuery : "/" + location.OriginalString.TrimStart('/');

    private static Regex HiddenInput(string name) => new($"<input[^>]*name=\"{name}\"[^>]*value=\"(?<value>[^\"]+)\"");

    private async Task EnsureSeasonsAsync()
    {
        await SeasonsCreated.WaitAsync(CancellationToken);
        try
        {
            if (s_seasonsExist)
            {
                return;
            }

            await using var db = await fixture.CreateDbContextAsync();
            var open = Season.Create(2097, slotsPerDay: 2, DateTimeOffset.UtcNow);
            open.ClaimsOpenAt = DateTimeOffset.UtcNow.AddDays(-1);
            open.Slots.Single(s => s is { Day: 3, Part: 1 }).AuthorName = "Already Taken";
            db.Seasons.AddRange(open, Season.Create(2098, slotsPerDay: 2, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(CancellationToken);
            s_seasonsExist = true;
        }
        finally
        {
            SeasonsCreated.Release();
        }
    }
}
