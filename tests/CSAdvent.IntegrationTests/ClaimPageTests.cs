using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The claim form, posted the way a browser does. Season 2097 has claims open, with December 3, part 1 already claimed;
/// season 2098 hasn't opened claims. Both are later than the 2089 season the home page tests rely on.
/// </summary>
public sealed partial class ClaimPageTests(AppHostFixture fixture)
{
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
    public async Task TheFormShowsTheSpotTheVerifiedEmailAndTheGitHubName()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-viewer", 6001, name: "Vera Viewer");

        var html = await browser.GetStringAsync("/claim/2097/2/1", CancellationToken);

        Assert.Contains(">Request December 2, 2097</h1>", html);
        Assert.Contains("claim-viewer@example.com", html);
        Assert.Contains("value=\"Vera Viewer\"", html);
        Assert.Contains("<a href=\"terms\" target=\"_blank\"", html);
        Assert.Contains("<a href=\"privacy\" target=\"_blank\"", html);
    }

    [Fact]
    public async Task TheFormShowsTheBlurbRuleWithTheExample()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-blurb", 6031);

        var html = await browser.GetStringAsync("/claim/2097/2/1", CancellationToken);

        Assert.Contains("Your post opens with the C# Advent blurb.", html);
        Assert.Contains("This post is part of the <a href=\"https://csadvent.christmas\" class=\"text-advent-700 underline\">2097 C# Advent calendar</a>", html);
        Assert.Contains("it opens with the C# Advent blurb, linking back to the", html);
        Assert.DoesNotContain("it links back to C# Advent", html);
    }

    [Fact]
    public async Task SocialAccountsAreSavedOnTheContributorAndPrefilledNextTime()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-social", 6007);

        var response = await PostClaimAsync(browser, "/claim/2097/7/1", new()
        {
            ["Form.DisplayName"] = "Sol Social",
            ["Form.Bluesky"] = "@Sol.bsky.social",
            ["Form.Mastodon"] = "https://hachyderm.io/@sol",
            ["Form.LinkedIn"] = "linkedin.com/in/sol-social",
            ["Form.Facebook"] = "fb.com/sol.social",
            ["Form.Reddit"] = "u/sol_social",
            ["Form.X"] = "https://x.com/SolSocial",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = await fixture.CreateDbContextAsync();
        var contributor = await db.Contributors.SingleAsync(c => c.GithubUserId == 6007, CancellationToken);
        Assert.Equal(
            ("sol.bsky.social", "@sol@hachyderm.io", "https://www.linkedin.com/in/sol-social", "https://www.facebook.com/sol.social", "sol_social", "SolSocial"),
            (contributor.BlueskyHandle, contributor.MastodonHandle, contributor.LinkedInUrl, contributor.FacebookUrl, contributor.RedditUsername, contributor.XUsername));

        var nextForm = await browser.GetStringAsync("/claim/2097/8/1", CancellationToken);
        Assert.Contains("value=\"sol.bsky.social\"", nextForm);
        Assert.Contains("value=\"@sol@hachyderm.io\"", nextForm);
        Assert.Contains("value=\"SolSocial\"", nextForm);
    }

    [Fact]
    public async Task InvalidSocialAccountsAreReportedAndNothingIsStored()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-typo", 6008);

        var response = await PostClaimAsync(browser, "/claim/2097/9/1", new()
        {
            ["Form.DisplayName"] = "Typo",
            ["Form.Mastodon"] = "@typo",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a Mastodon handle", await response.Content.ReadAsStringAsync(CancellationToken));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.ClaimRequests.AnyAsync(r => r.Contributor.GithubUserId == 6008, CancellationToken));
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
    public async Task SubmittingEmailsTheContributorAndTheOrganizers()
    {
        await EnsureSeasonsAsync();
        using var browser = await SignInAsync("claim-grace", 6009);

        await PostClaimAsync(browser, "/claim/2097/20/2", new()
        {
            ["Form.DisplayName"] = "Grace Hopper",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        });

        var received = await fixture.WaitForEmailAsync("claim-grace@example.com", "We received your C# Advent request for December 20");
        Assert.Equal(("C# Advent", "donotreply@mail.csadvent.christmas"), (received.From.Name, received.From.Address));
        Assert.Equal("hello@csadvent.christmas", Assert.Single(received.ReplyTo).Address);
        Assert.Contains("Thanks, Grace Hopper!", received.Html);
        Assert.Contains("/images/csadvent-logo.png", received.Html);
        Assert.Contains("December 20, 2097 (post 2)", received.Text);

        // Replying to the organizers' copy reaches the contributor.
        var notice = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "New C# Advent request: Grace Hopper for December 20", containing: "claim-grace@example.com");
        Assert.Equal("claim-grace@example.com", Assert.Single(notice.ReplyTo).Address);
        await using var db = await fixture.CreateDbContextAsync();
        var requestId = await db.ClaimRequests.Where(r => r.Contributor.GithubUserId == 6009).Select(r => r.Id).SingleAsync(CancellationToken);
        Assert.Contains($"/manage/requests/{requestId}", notice.Html);
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

    private Task<HttpClient> SignInAsync(string login, long githubUserId, string? name = null) =>
        fixture.SignInAsync(login, githubUserId, name);

    private static Task<HttpResponseMessage> PostClaimAsync(HttpClient browser, string path, Dictionary<string, string> fields) =>
        browser.PostFormAsync(path, "claim", fields);

    private static string PathOf(Uri location) => TestBrowser.PathOf(location);

    private Task EnsureSeasonsAsync() => Task.WhenAll(
        TestSeasons.EnsureAsync(fixture, 2097, claimsOpen: true, season => season.Slots.Single(s => s is { Day: 3, Part: 1 }).AuthorName = "Already Taken"),
        TestSeasons.EnsureAsync(fixture, 2098, claimsOpen: false));
}
