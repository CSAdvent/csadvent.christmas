using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Web;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// GitHub sign-in's OAuth handshake, end to end (#199): the site's own account setup (<see cref="AccountEndpoints.AddAccounts"/>
/// and <see cref="AccountEndpoints.MapAccounts"/>) in a small host of its own, configured as in production, against the test
/// database. GitHub is played by a fake behind the OAuth handler's backchannel and the GitHub API client, so nothing
/// reaches github.com. The AppHost's web app can't do this: it has no GitHub app in the Testing environment.
/// </summary>
public sealed class GitHubSignInTests(AppHostFixture fixture)
{
    private const string ClientId = "test-client-id";
    private const string ClientSecret = "test-client-secret";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SigningInWithGitHubRecordsTheContributorWithTheirPrimaryVerifiedEmail()
    {
        var github = new FakeGitHub(new GitHubUser(262301, "oauth-olive", "Olive OAuth"))
        {
            Emails = [new("olive-old@example.com", Primary: false, Verified: true), new("olive@example.com", Primary: true, Verified: true)],
            SocialAccounts = [new("mastodon", "https://hachyderm.io/@olive")],
        };
        await using var site = await Site.StartAsync(fixture, github);

        // /signin sends them to GitHub, asking for their email addresses, with GitHub coming back to /signin-github.
        var (challenge, cookies) = await site.GetAsync("/signin?returnUrl=%2Fmy-spots", []);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorize = challenge.Headers.Location!;
        Assert.Equal("https://github.com/login/oauth/authorize", authorize.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal((ClientId, "code", "user:email", $"{site.Address}/signin-github"), (query["client_id"], query["response_type"], query["scope"], query["redirect_uri"]));

        // GitHub sends them back with a code, which is exchanged for a token, then their account and emails are read.
        var (callback, signedIn) = await site.GetAsync($"/signin-github?code=code-262301&state={Uri.EscapeDataString(query["state"]!)}", cookies);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/my-spots", callback.Headers.Location!.OriginalString);

        var token = Assert.Single(github.Requests, r => r.Url == GitHubSignIn.TokenEndpoint);
        Assert.Equal(HttpMethod.Post, token.Method);
        var form = HttpUtility.ParseQueryString(token.Body!);
        Assert.Equal(("code-262301", ClientId, ClientSecret, "authorization_code"), (form["code"], form["client_id"], form["client_secret"], form["grant_type"]));
        Assert.All(github.Requests.Where(r => r.Url is GitHubSignIn.UserEndpoint or GitHubSignIn.EmailsEndpoint), r => Assert.Equal("Bearer gho-262301", r.Authorization));

        // Their cookie names them, with only the primary verified address and their contributor record.
        int contributorId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributor = await db.Contributors.AsNoTracking().SingleAsync(c => c.GithubUserId == 262301, CancellationToken);
            Assert.Equal(("oauth-olive", "olive@example.com"), (contributor.GithubLogin, contributor.Email));
            Assert.NotNull(contributor.EmailVerifiedAt);

            // The first time, their GitHub profile's social links fill in their accounts (#98).
            Assert.Equal("@olive@hachyderm.io", contributor.MastodonHandle);
            Assert.NotNull(contributor.GithubSocialCheckedAt);
            contributorId = contributor.Id;
        }

        Assert.Equal(
            new Dictionary<string, string>
            {
                [ClaimTypes.NameIdentifier] = "262301",
                [ClaimTypes.Name] = "oauth-olive",
                [ClaimTypes.Email] = "olive@example.com",
                [AdventClaims.GitHubName] = "Olive OAuth",
                [AdventClaims.ContributorId] = $"{contributorId}",
            },
            await site.WhoAmIAsync(signedIn));

        // Next time they've renamed their account and their primary address isn't verified: the same contributor, under the
        // new login, with no address at all rather than another of theirs (#168), and their profile isn't filled in again.
        github.User = new GitHubUser(262301, "oauth-olivia", null);
        github.Emails = [new("olive-old@example.com", Primary: false, Verified: true), new("olivia@example.com", Primary: true, Verified: false)];
        var again = await site.SignInAsync(github, "/");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributor = await db.Contributors.AsNoTracking().SingleAsync(c => c.GithubUserId == 262301, CancellationToken);
            Assert.Equal((contributorId, "oauth-olivia", (string?)null, (DateTimeOffset?)null), (contributor.Id, contributor.GithubLogin, contributor.Email, contributor.EmailVerifiedAt));
        }

        var claims = await site.WhoAmIAsync(again);
        Assert.Equal(("oauth-olivia", $"{contributorId}"), (claims[ClaimTypes.Name], claims[AdventClaims.ContributorId]));
        Assert.False(claims.ContainsKey(ClaimTypes.Email));
        Assert.Single(github.Requests, r => r.Url.EndsWith("/social_accounts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReturnAddressOnAnotherSiteComesBackHome()
    {
        var github = new FakeGitHub(new GitHubUser(262302, "oauth-otto", "Otto OAuth")) { Emails = [new("otto@example.com", Primary: true, Verified: true)] };
        await using var site = await Site.StartAsync(fixture, github);

        var (challenge, cookies) = await site.GetAsync("/signin?returnUrl=https%3A%2F%2Fevil.example%2F", []);
        var state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"]!;
        var (callback, _) = await site.GetAsync($"/signin-github?code=code-262302&state={Uri.EscapeDataString(state)}", cookies);

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ACallbackWithoutTheSignInsCookieSignsNobodyIn()
    {
        var github = new FakeGitHub(new GitHubUser(262303, "oauth-ozzy", null)) { Emails = [new("ozzy@example.com", Primary: true, Verified: true)] };
        await using var site = await Site.StartAsync(fixture, github);

        // A callback made up elsewhere (cross-site request forgery): the state is real, but this browser never started it.
        var (challenge, _) = await site.GetAsync("/signin?returnUrl=%2F", []);
        var state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"]!;
        var (callback, cookies) = await site.GetAsync($"/signin-github?code=code-262303&state={Uri.EscapeDataString(state)}", []);

        Assert.NotEqual(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.DoesNotContain(cookies, c => c.StartsWith("csadvent.auth", StringComparison.Ordinal));
        Assert.DoesNotContain(github.Requests, r => r.Url == GitHubSignIn.TokenEndpoint);
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.Contributors.AnyAsync(c => c.GithubUserId == 262303, CancellationToken));
    }

    /// <summary>
    /// The site's sign-in in a host of its own, on a free port on this machine, with a page that shows the signed-in user's
    /// claims. Cookies are handled by hand: the OAuth correlation cookie is Secure, which a client only sends over HTTPS.
    /// </summary>
    private sealed class Site : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _client;

        private Site(WebApplication app, string address)
        {
            _app = app;
            Address = address;
            _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = new Uri(address) };
        }

        public string Address { get; }

        public static async Task<Site> StartAsync(AppHostFixture fixture, FakeGitHub github)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:GitHub:ClientId"] = ClientId,
                ["Accounts:GitHub:ClientSecret"] = ClientSecret,
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.AddAccounts();
            var connectionString = await fixture.GetConnectionStringAsync();
            builder.Services.AddDbContext<AdventDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<ProfileService>();
            builder.Services.AddHttpClient<GitHubUsers>(GitHubUsers.Configure).ConfigurePrimaryHttpMessageHandler(() => github.Handler());
            builder.Services.AddAntiforgery();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // GitHub's side of the handshake: the token exchange and the API calls the sign-in makes.
            builder.Services.Configure<OAuthOptions>(GitHubSignIn.Scheme, options => options.BackchannelHttpHandler = github.Handler());

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAccounts();
            app.MapGet("/whoami", (ClaimsPrincipal user) => user.Claims.ToDictionary(c => c.Type, c => c.Value));
            await app.StartAsync(CancellationToken);
            return new Site(app, app.Urls.Single().TrimEnd('/'));
        }

        /// <summary>A GET with these cookies, and the cookies after it (theirs, updated by what it set).</summary>
        public async Task<(HttpResponseMessage Response, string[] Cookies)> GetAsync(string path, string[] cookies)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (cookies.Length > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", cookies));
            }

            var response = await _client.SendAsync(request, CancellationToken);
            var set = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Select(v => v.Split(';')[0]).ToList() : [];
            var kept = cookies.Where(c => !set.Any(s => Name(s) == Name(c))).Concat(set.Where(s => !s.EndsWith('=')));
            return (response, [.. kept]);
        }

        /// <summary>Goes through the whole handshake, and returns the signed-in cookies.</summary>
        public async Task<string[]> SignInAsync(FakeGitHub github, string returnUrl)
        {
            var (challenge, cookies) = await GetAsync($"/signin?returnUrl={Uri.EscapeDataString(returnUrl)}", []);
            var state = HttpUtility.ParseQueryString(challenge.Headers.Location!.Query)["state"]!;
            var (callback, signedIn) = await GetAsync($"/signin-github?code=code-{github.User.Id}&state={Uri.EscapeDataString(state)}", cookies);
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            return signedIn;
        }

        public async Task<Dictionary<string, string>> WhoAmIAsync(string[] cookies)
        {
            var (response, _) = await GetAsync("/whoami", cookies);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(CancellationToken))!;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
        }

        private static string Name(string cookie) => cookie.Split('=')[0];
    }

    /// <summary>GitHub's OAuth token endpoint and API, as far as the sign-in uses them, recording every request.</summary>
    private sealed class FakeGitHub(GitHubUser user)
    {
        public GitHubUser User { get; set; } = user;

        public GitHubEmail[] Emails { get; set; } = [];

        public GitHubSocialAccount[] SocialAccounts { get; set; } = [];

        public ConcurrentQueue<GitHubRequest> Requests { get; } = new();

        public HttpMessageHandler Handler() => new Responder(this);

        private sealed class Responder(FakeGitHub github) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                github.Requests.Enqueue(new GitHubRequest(request.Method, url, request.Headers.Authorization?.ToString(), body));
                var user = github.User;
                object? answer = (request.Method.Method, url) switch
                {
                    ("POST", GitHubSignIn.TokenEndpoint) => new Dictionary<string, string>
                    {
                        ["access_token"] = $"gho-{user.Id}",
                        ["token_type"] = "bearer",
                        ["scope"] = "user:email",
                    },
                    ("GET", GitHubSignIn.UserEndpoint) => new { id = user.Id, login = user.Login, name = user.Name },
                    ("GET", GitHubSignIn.EmailsEndpoint) => github.Emails.Select(e => new { email = e.Email, primary = e.Primary, verified = e.Verified }),
                    ("GET", _) when url == $"https://api.github.com/users/{user.Login}/social_accounts" => github.SocialAccounts.Select(a => new { provider = a.Provider, url = a.Url }),
                    _ => null,
                };

                return answer is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(answer) };
            }
        }
    }

    private sealed record GitHubRequest(HttpMethod Method, string Url, string? Authorization, string? Body);
}
