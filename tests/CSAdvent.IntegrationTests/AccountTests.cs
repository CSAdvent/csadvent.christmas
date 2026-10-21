using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>Sign-in through dev sign-in (GitHub isn't configured in tests), cookie sessions, and the organizer area.</summary>
public sealed partial class AccountTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnonymousVisitorsAreSentToSignInForTheAdminArea()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/manage", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?ReturnUrl=%2Fmanage", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task SignInWithoutGitHubOffersDevSignIn()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/signin?returnUrl=/archive", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/dev/signin-options?returnUrl=%2Farchive", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SigningInRecordsTheContributorAndShowsTheirName()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/dev/signin?login=ada-dev&id=5001&returnUrl=/archive", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/archive", response.Headers.Location!.OriginalString);
        var home = await browser.GetStringAsync("/", CancellationToken);
        Assert.Contains("ada-dev", home);
        Assert.Contains("Sign out", home);

        // Signing in again under a new login updates the same contributor.
        await browser.GetAsync("/dev/signin?login=ada-renamed&id=5001", CancellationToken);
        await using var db = await fixture.CreateDbContextAsync();
        var contributor = await db.Contributors.SingleAsync(c => c.GithubUserId == 5001, CancellationToken);
        Assert.Equal("ada-renamed", contributor.GithubLogin);
        Assert.Equal("ada-renamed@example.com", contributor.Email);
        Assert.NotNull(contributor.EmailVerifiedAt);
    }

    [Fact]
    public async Task ContributorsCannotOpenTheAdminArea()
    {
        using var browser = fixture.CreateBrowser();
        await browser.GetAsync("/dev/signin?login=bob-dev&id=5002", CancellationToken);

        var response = await browser.GetAsync("/manage", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task OrganizersCanOpenTheAdminArea()
    {
        using var browser = fixture.CreateBrowser();
        await browser.GetAsync($"/dev/signin?login=organizer&id={AppHostFixture.OrganizerGitHubId}", CancellationToken);

        var response = await browser.GetAsync("/manage", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">Manage</h1>", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task SigningOutEndsTheSession()
    {
        using var browser = fixture.CreateBrowser();
        await browser.GetAsync("/dev/signin?login=carol-dev&id=5003", CancellationToken);
        var token = AntiforgeryToken().Match(await browser.GetStringAsync("/", CancellationToken)).Groups["token"].Value;

        var response = await browser.PostAsync(
            "/signout",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
            CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var home = await browser.GetStringAsync("/", CancellationToken);
        Assert.Contains(">Sign in</a>", home);
        Assert.DoesNotContain("Sign out", home);
    }

    [Fact]
    public async Task SigningOutNeedsTheAntiforgeryToken()
    {
        using var browser = fixture.CreateBrowser();
        await browser.GetAsync("/dev/signin?login=dave-dev&id=5004", CancellationToken);

        var response = await browser.PostAsync("/signout", new FormUrlEncodedContent([]), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignInNeverRedirectsOffSite()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/dev/signin?login=erin-dev&id=5005&returnUrl=//evil.example/", CancellationToken);

        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
