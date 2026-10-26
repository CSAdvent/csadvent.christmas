using System.Net.Http.Headers;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Posts to C# Advent's Mastodon account with <c>POST /api/v1/statuses</c>. Mastodon builds the link card itself from the
/// entry page's preview tags. The idempotency key stops a retried request posting twice, but Mastodon only keeps it for an
/// hour, so a try that may have gone out still waits for an organizer to check (#154).
/// </summary>
public sealed class MastodonPublisher(HttpClient http, string server, string accessToken) : ISocialPublisher
{
    public SocialPlatform Platform => SocialPlatform.Mastodon;

    public async Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, "api/v1/statuses");
        request.Headers.Add("Idempotency-Key", publication.IdempotencyKey);
        request.Content = new FormUrlEncodedContent(
        [
            new("status", publication.Announcement.Text),
            new("visibility", "public"),
            new("language", "en"),
        ]);

        return (await http.SendPostAsync<Status>(request, Platform, cancellationToken))?.Url;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, "api/v1/accounts/verify_credentials");
        using var response = await http.SendAsync(request, cancellationToken);
        await SocialPublishException.EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpRequestMessage Request(HttpMethod method, string path) =>
        new(method, $"https://{server}/{path}") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) } };

    private sealed record Status(string? Url);
}
