using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace CSAdvent.Web.Auth;

/// <summary>Turns a completed GitHub sign-in into a contributor and the claims in their cookie.</summary>
public static class GitHubSignIn
{
    public const string Scheme = "GitHub";

    public const string AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
    public const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    public const string UserEndpoint = "https://api.github.com/user";
    public const string EmailsEndpoint = "https://api.github.com/user/emails";

    public static async Task OnCreatingTicketAsync(OAuthCreatingTicketContext context)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        var user = await GetAsync<GitHubUser>(context, UserEndpoint, cancellationToken);
        var emails = await GetAsync<GitHubEmail[]>(context, EmailsEndpoint, cancellationToken);
        var verifiedEmail = SelectVerifiedEmail(emails);

        var contributors = context.HttpContext.RequestServices.GetRequiredService<ContributorSignIn>();
        var contributorId = await contributors.RecordAsync(user.Id, user.Login, verifiedEmail, cancellationToken);

        context.Identity!.AddClaims(AdventClaims.For(user.Id, user.Login, verifiedEmail, contributorId));
    }

    /// <summary>The primary address if GitHub has verified it, otherwise any verified address, otherwise none.</summary>
    public static string? SelectVerifiedEmail(IEnumerable<GitHubEmail> emails)
    {
        var verified = emails.Where(e => e.Verified).ToList();
        return (verified.FirstOrDefault(e => e.Primary) ?? verified.FirstOrDefault())?.Email;
    }

    private static async Task<T> GetAsync<T>(OAuthCreatingTicketContext context, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("csadvent.christmas", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await context.Backchannel.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException($"GitHub returned an empty response from {url}.");
    }
}

public sealed record GitHubUser(long Id, string Login);

public sealed record GitHubEmail(string Email, bool Primary, bool Verified);
