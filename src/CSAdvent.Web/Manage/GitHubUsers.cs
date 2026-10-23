using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CSAdvent.Web.Auth;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Manage;

public sealed record GitHubAccount(long Id, string Login);

/// <summary>One of the social links on a GitHub profile. GitHub names the provider it recognizes, or says "generic".</summary>
public sealed record GitHubSocialAccount(string Provider, string Url);

/// <summary>
/// Looks up public GitHub accounts: by login, so organizers can link archived posts to people who haven't signed in yet, and
/// the social links on a profile, to fill in a contributor's accounts. Uses the OAuth app's credentials when they're
/// configured, which raises GitHub's limit from 60 to 5,000 lookups an hour.
/// </summary>
public sealed class GitHubUsers(HttpClient http, IOptions<AccountOptions> accounts)
{
    /// <summary>The account, or null if there's no such login.</summary>
    public async Task<GitHubAccount?> FindAsync(string login, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync($"users/{Uri.EscapeDataString(login)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitHubAccount>(cancellationToken);
    }

    /// <summary>The social links on the account's public profile; none if there's no such login.</summary>
    public async Task<IReadOnlyList<GitHubSocialAccount>> SocialAccountsAsync(string login, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync($"users/{Uri.EscapeDataString(login)}/social_accounts", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GitHubSocialAccount>>(cancellationToken) ?? [];
    }

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accounts.Value.GitHub is { IsConfigured: true } app)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{app.ClientId}:{app.ClientSecret}")));
        }

        return await http.SendAsync(request, cancellationToken);
    }

    public static void Configure(HttpClient http)
    {
        http.BaseAddress = new Uri("https://api.github.com/");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
}
