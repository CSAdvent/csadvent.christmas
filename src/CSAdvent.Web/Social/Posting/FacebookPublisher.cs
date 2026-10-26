using System.Net.Http.Headers;
using CSAdvent.Web.Claims;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Posts to the C# Advent Facebook Page with <c>POST /{page-id}/feed</c>, as the Page. Facebook builds the link card from
/// the entry page's preview tags. The Page token goes in the Authorization header, so it's never in a logged URL.
/// </summary>
public sealed class FacebookPublisher(HttpClient http, FacebookCredentials credentials) : ISocialPublisher
{
    public SocialPlatform Platform => SocialPlatform.Facebook;

    public async Task<string?> PublishAsync(SocialPublication publication, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"{credentials.PageId}/feed");
        request.Content = new FormUrlEncodedContent(
        [
            new("message", publication.Announcement.Text),
            new("link", publication.Announcement.Link),
        ]);

        // The ID is "{page-id}_{post-id}", which is also the post's address on Facebook. There's no idempotency key, so a try
        // that may have gone out waits for an organizer to check (#154).
        return (await http.SendPostAsync<Created>(request, Platform, cancellationToken))?.Id is { Length: > 0 } id
            ? $"https://www.facebook.com/{Uri.EscapeDataString(id)}"
            : null;
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"{credentials.PageId}?fields=id,name");
        using var response = await http.SendAsync(request, cancellationToken);
        await SocialPublishException.EnsureSuccessAsync(response, cancellationToken);
    }

    private HttpRequestMessage Request(HttpMethod method, string path) =>
        new(method, $"https://graph.facebook.com/{credentials.GraphVersion}/{path}")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", credentials.PageToken) },
        };

    private sealed record Created(string? Id);
}
