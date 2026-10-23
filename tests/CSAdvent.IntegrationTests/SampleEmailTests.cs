using System.Net;

namespace CSAdvent.IntegrationTests;

/// <summary>Organizers can preview every email and send any of them now, with sample data, to someone on the list.</summary>
public sealed class SampleEmailTests(AppHostFixture fixture)
{
    private const string Page = "/manage/emails";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrganizersSendAnyEmailToThemselvesNow()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = await organizer.GetStringAsync(Page, CancellationToken);
        Assert.Contains("value=\"organizer@example.com\"", page);
        Assert.Contains("href=\"manage/emails/claim-received\"", page);

        var sent = await organizer.PostFormAsync(Page, "send", new() { ["Form.Key"] = "claim-received", ["Form.To"] = "organizer@example.com" });

        Assert.Equal($"{Page}?done=sent", TestBrowser.PathOf(sent.Headers.Location!));
        var email = await fixture.WaitForEmailAsync("organizer@example.com", "[Sample] We received your C# Advent request");
        Assert.Contains("Ada Lovelace", email.Html);
    }

    [Fact]
    public async Task SamplesOnlyGoToPeopleOnTheList()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var refused = await organizer.PostFormAsync(Page, "send", new() { ["Form.Key"] = "claim-received", ["Form.To"] = "someone-else@example.com" });

        Assert.Equal($"{Page}?done=not-allowed", TestBrowser.PathOf(refused.Headers.Location!));
        Assert.Equal(0, await fixture.CountEmailsAsync("someone-else@example.com", "[Sample]"));
    }

    [Fact]
    public async Task OrganizersPreviewEmailsAndNobodyElseCan()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var preview = await organizer.GetAsync($"{Page}/claim-received", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Ada Lovelace", await preview.Content.ReadAsStringAsync(CancellationToken));

        using var contributor = await fixture.SignInAsync("sample-nosy", 8301);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf((await contributor.GetAsync(Page, CancellationToken)).Headers.Location!));
        Assert.NotEqual(HttpStatusCode.OK, (await contributor.GetAsync($"{Page}/claim-received", CancellationToken)).StatusCode);
    }
}
