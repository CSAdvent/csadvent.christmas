using System.Security.Claims;
using CSAdvent.Web.Auth;
using CSAdvent.Web.SeasonTesting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Auth;

/// <summary>The sign-in cookie (#192): Secure in production, 14 days for everyone, 12 hours for organizers.</summary>
public sealed class AccountSessionTests
{
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    private const long Organizer = 1;
    private const long Author = 2;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 12, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly AccountOptions _accounts = new() { Admins = [Organizer] };

    [Fact]
    public async Task AnOrganizersSignInLastsTwelveHoursHoweverBusyTheyAre()
    {
        var services = Services(Environments.Production);
        var cookie = await SignInAsync(services, Organizer);

        var ticket = Read(services, cookie);
        Assert.Equal(_clock.GetUtcNow() + AccountSessions.OrganizerSession, ticket.Properties.ExpiresUtc);
        Assert.False(ticket.Properties.AllowRefresh);

        _clock.Advance(TimeSpan.FromHours(11));
        Assert.True((await AuthenticateAsync(services, cookie)).Result.Succeeded);

        _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        Assert.False((await AuthenticateAsync(services, cookie)).Result.Succeeded);
    }

    [Fact]
    public async Task EveryoneElseStaysSignedInForFourteenDaysAfterTheirLastVisit()
    {
        var services = Services(Environments.Production);
        var cookie = await SignInAsync(services, Author);

        var ticket = Read(services, cookie);
        Assert.Equal(_clock.GetUtcNow() + AccountSessions.Session, ticket.Properties.ExpiresUtc);
        Assert.NotEqual(false, ticket.Properties.AllowRefresh);

        _clock.Advance(TimeSpan.FromDays(13));
        Assert.True((await AuthenticateAsync(services, cookie)).Result.Succeeded);
    }

    [Fact]
    public async Task SomeoneMadeAnOrganizerWhileSignedInSignsInAgain()
    {
        // Signed in as an author, with an everyday 14-day sign-in, then added to the organizers (or signed in before #192).
        var services = Services(Environments.Production);
        var cookie = await SignInAsync(services, Author);
        _accounts.Admins = [Organizer, Author];

        var (result, context) = await AuthenticateAsync(services, cookie);

        Assert.False(result.Succeeded);
        Assert.Contains($"{AccountSessions.CookieName}=;", context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task TheSignInCookieIsSecureInProductionEvenOverPlainHttp()
    {
        var cookie = await SignInAsync(Services(Environments.Production), Author, https: false);

        Assert.Contains("secure", cookie.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie.SetCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DevelopmentFollowsTheRequestSoPlainHttpTestsKeepTheirCookie()
    {
        var services = Services(Environments.Development);

        Assert.DoesNotContain("secure", (await SignInAsync(services, Author, https: false)).SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", (await SignInAsync(services, Author, https: true)).SetCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Production", CookieSecurePolicy.Always)]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    public void GitHubSignInUsesPkceAndEveryCookieIsSecureInProduction(string environment, CookieSecurePolicy expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Accounts:GitHub:ClientId"] = "client-id",
            ["Accounts:GitHub:ClientSecret"] = "client-secret",
        });
        builder.AddAccounts();

        // Only the options are wanted here, not the database the sign-in services use.
        builder.Host.UseDefaultServiceProvider(provider => provider.ValidateOnBuild = false);
        using var app = builder.Build();

        var github = app.Services.GetRequiredService<IOptionsMonitor<OAuthOptions>>().Get(GitHubSignIn.Scheme);
        Assert.True(github.UsePkce);
        Assert.Equal(CookieSecurePolicy.Always, github.CorrelationCookie.SecurePolicy);
        Assert.Equal(expected, app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).Cookie.SecurePolicy);
        Assert.Equal(expected, app.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
    }

    [Fact]
    public void OnlyAnUnrenewedTwelveHourSignInIsAnOrganizers()
    {
        var issued = _clock.GetUtcNow();

        Assert.True(AccountSessions.IsOrganizerSession(new() { IssuedUtc = issued, ExpiresUtc = issued.AddHours(12), AllowRefresh = false }));
        Assert.False(AccountSessions.IsOrganizerSession(new() { IssuedUtc = issued, ExpiresUtc = issued.AddHours(12) }));
        Assert.False(AccountSessions.IsOrganizerSession(new() { IssuedUtc = issued, ExpiresUtc = issued.AddDays(14), AllowRefresh = false }));
    }

    private ServiceProvider Services(string environment)
    {
        var host = new FakeEnvironment { EnvironmentName = environment };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IHostEnvironment>(host);
        services.AddSingleton<IOptionsMonitor<AccountOptions>>(new StaticOptionsMonitor(_accounts));
        services.AddSingleton(new TestSeasons(null!));
        services.AddAuthentication(Scheme).AddCookie(cookie =>
        {
            AccountSessions.Configure(cookie, host);
            cookie.TimeProvider = _clock;
        });
        return services.BuildServiceProvider();
    }

    private static async Task<SignedInCookie> SignInAsync(ServiceProvider services, long githubUserId, bool https = true)
    {
        // A scope per request, as ASP.NET Core does: authentication handlers belong to one request.
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = https ? "https" : "http";
        var user = new ClaimsPrincipal(new ClaimsIdentity(AdventClaims.For(githubUserId, $"user{githubUserId}", null, contributorId: 1), Scheme));

        await context.SignInAsync(Scheme, user);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        var value = setCookie[(AccountSessions.CookieName.Length + 1)..setCookie.IndexOf(';', StringComparison.Ordinal)];
        return new SignedInCookie(value, setCookie);
    }

    private static async Task<(AuthenticateResult Result, HttpContext Context)> AuthenticateAsync(ServiceProvider services, SignedInCookie cookie)
    {
        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Headers.Cookie = $"{AccountSessions.CookieName}={cookie.Value}";
        return (await context.AuthenticateAsync(Scheme), context);
    }

    private static AuthenticationTicket Read(ServiceProvider services, SignedInCookie cookie) =>
        services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).TicketDataFormat.Unprotect(cookie.Value)
            ?? throw new InvalidOperationException("The cookie couldn't be read.");

    private sealed record SignedInCookie(string Value, string SetCookie);

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "CSAdvent.Web";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class StaticOptionsMonitor(AccountOptions value) : IOptionsMonitor<AccountOptions>
    {
        public AccountOptions CurrentValue => value;

        public AccountOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AccountOptions, string?> listener) => null;
    }
}
