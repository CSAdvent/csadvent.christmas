using System.Security.Claims;
using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Auth;

public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/archive/2017")]
    [InlineData("/manage?tab=requests#top")]
    public void KeepsPathsOnThisSite(string url) => Assert.Equal(url, ReturnUrl.Sanitize(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("archive")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("https://evil.example/")]
    [InlineData("javascript:alert(1)")]
    public void SendsEverythingElseHome(string? url) => Assert.Equal("/", ReturnUrl.Sanitize(url));
}

public sealed class GitHubSignInTests
{
    [Fact]
    public void PrefersThePrimaryVerifiedEmail()
    {
        string? email = GitHubSignIn.SelectVerifiedEmail([
            new("old@example.com", Primary: false, Verified: true),
            new("main@example.com", Primary: true, Verified: true),
        ]);

        Assert.Equal("main@example.com", email);
    }

    [Fact]
    public void FallsBackToAnotherVerifiedEmail()
    {
        string? email = GitHubSignIn.SelectVerifiedEmail([
            new("unverified@example.com", Primary: true, Verified: false),
            new("verified@example.com", Primary: false, Verified: true),
        ]);

        Assert.Equal("verified@example.com", email);
    }

    [Fact]
    public void NeverUsesAnUnverifiedEmail()
    {
        Assert.Null(GitHubSignIn.SelectVerifiedEmail([new("unverified@example.com", Primary: true, Verified: false)]));
    }
}

public sealed class AdminAuthorizationHandlerTests
{
    private static async Task<bool> IsAdminAsync(ClaimsPrincipal user, params long[] admins)
    {
        var handler = new AdminAuthorizationHandler(new StaticOptionsMonitor(new AccountOptions { Admins = admins }));
        var context = new AuthorizationHandlerContext([new AdminRequirement()], user, resource: null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private static ClaimsPrincipal SignedIn(long githubUserId) =>
        new(new ClaimsIdentity(AdventClaims.For(githubUserId, "someone", verifiedEmail: null, contributorId: 1), "test"));

    [Fact]
    public async Task AllowsConfiguredOrganizers() => Assert.True(await IsAdminAsync(SignedIn(42), 7, 42));

    [Fact]
    public async Task RefusesEveryoneElse() => Assert.False(await IsAdminAsync(SignedIn(43), 7, 42));

    [Fact]
    public async Task RefusesAnonymousUsers() => Assert.False(await IsAdminAsync(new ClaimsPrincipal(new ClaimsIdentity()), 42));

    private sealed class StaticOptionsMonitor(AccountOptions value) : IOptionsMonitor<AccountOptions>
    {
        public AccountOptions CurrentValue => value;

        public AccountOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AccountOptions, string?> listener) => null;
    }
}
