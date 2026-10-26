namespace CSAdvent.IntegrationTests;

/// <summary>
/// The legacy site's remaining addresses (#189), and the backup list explaining itself before anyone signs in. Uses season
/// 2441 and GitHub user ID 244101.
/// </summary>
public sealed class LegacyAddressTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/mailinglist", "/announcements", "text/html")]
    [InlineData("/images/csadventSocialCard.png", "/images/social-card.jpg", "image/jpeg")]
    [InlineData("/images/csadvent%20logo.png", "/images/csadvent-logo.png", "image/png")]
    public async Task OldAddressesRedirectPermanently(string path, string target, string mediaType)
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(target, response.Headers.Location!.OriginalString);
        var moved = await browser.GetAsync(target, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(mediaType, moved.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task TheBackupListExplainsItselfBeforeAskingToSignIn()
    {
        await TestSeasons.EnsureAsync(fixture, 2441, claimsOpen: true);
        using var reader = fixture.CreateBrowser();

        var page = await reader.GetPageAsync("/backups/2441");

        Assert.Contains("Backups step in when an author drops out", page);
        Assert.Contains("href=\"signin?returnUrl=%2Fbackups%2F2441\"", page);
        Assert.DoesNotContain("Join the backup list</button>", page);

        // Signed in, the form is there as before.
        using var backup = await fixture.SignInAsync("backup-bea", 244101);
        var signedIn = await backup.GetPageAsync("/backups/2441");
        Assert.Contains("Join the backup list</button>", signedIn);
        Assert.DoesNotContain("href=\"signin?", signedIn);
    }

    [Fact]
    public async Task AFormPostedWithoutSigningInGoesToSignIn()
    {
        await TestSeasons.EnsureAsync(fixture, 2441, claimsOpen: true);
        using var reader = fixture.CreateBrowser();

        // A token from a page anyone can post from, as if the sign-in had run out since the form loaded.
        var announcements = await reader.GetPageAsync("/announcements");
        var response = await reader.PostAsync("/backups/2441", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = TestBrowser.HiddenValue(announcements, "__RequestVerificationToken"),
            ["_handler"] = "backup",
            ["Form.Name"] = "Nobody",
            ["Form.AgreeToTerms"] = "true",
        }), CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/signin?returnUrl=%2Fbackups%2F2441", TestBrowser.PathOf(response.Headers.Location!));
    }
}
