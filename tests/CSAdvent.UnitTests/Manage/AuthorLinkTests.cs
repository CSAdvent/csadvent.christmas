using System.Net;
using System.Text;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Manage;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Manage;

public sealed class AuthorLinkTests
{
    [Theory]
    [InlineData("Ada Lovelace", "ada lovelace")]
    [InlineData("  Ada   LOVELACE ", "ada lovelace")]
    public void AuthorsAreGroupedByNameWhateverItsCaseOrSpacing(string name, string key) =>
        Assert.Equal(key, AuthorLinkService.Key(name));

    [Theory]
    [InlineData("https://github.com/Ada-Lovelace", "ada-lovelace")]
    [InlineData("https://www.github.com/ada/", "ada")]
    [InlineData("https://github.com/ada/csharp-advent", null)]
    [InlineData("https://twitter.com/ada", null)]
    [InlineData(null, null)]
    public void GitHubProfileLinksSuggestALogin(string? url, string? login) =>
        Assert.Equal(login, AuthorLinkService.SuggestLogin(url));

    [Fact]
    public async Task FindsAGitHubAccountWithTheAppsCredentials()
    {
        var github = new FakeGitHub("""{ "id": 583231, "login": "octocat" }""", HttpStatusCode.OK);

        var user = await Users(github, new GitHubAppOptions { ClientId = "id", ClientSecret = "secret" }).FindAsync("octocat", TestContext.Current.CancellationToken);

        Assert.Equal(new GitHubAccount(583231, "octocat"), user);
        Assert.Equal("https://api.github.com/users/octocat", github.Request!.RequestUri!.ToString());
        Assert.Equal(("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("id:secret"))), (github.Request.Headers.Authorization!.Scheme, github.Request.Headers.Authorization.Parameter));
    }

    [Fact]
    public async Task AnUnknownLoginIsNoAccount()
    {
        var github = new FakeGitHub("""{ "message": "Not Found" }""", HttpStatusCode.NotFound);

        Assert.Null(await Users(github, new GitHubAppOptions()).FindAsync("nobody-here", TestContext.Current.CancellationToken));
        Assert.Null(github.Request!.Headers.Authorization);
    }

    private static GitHubUsers Users(FakeGitHub github, GitHubAppOptions app)
    {
        var http = new HttpClient(github);
        GitHubUsers.Configure(http);
        return new GitHubUsers(http, Options.Create(new AccountOptions { GitHub = app }));
    }

    private sealed class FakeGitHub(string json, HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
