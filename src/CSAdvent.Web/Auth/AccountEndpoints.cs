using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Auth;

/// <summary>Sign-in with GitHub (or dev sign-in locally), cookie sessions, and the organizer policy.</summary>
public static class AccountEndpoints
{
    public const string DevSignInPagePath = "/dev/signin-options";

    public static WebApplicationBuilder AddAccounts(this WebApplicationBuilder builder)
    {
        // Checked when the app starts rather than when it's built, so migration bundles can build the host without GitHub settings.
        builder.Services.AddOptions<AccountOptions>()
            .BindConfiguration(AccountOptions.SectionName)
            .Validate(
                options => builder.Environment.IsDevelopment() || options.GitHub.IsConfigured,
                $"Accounts:GitHub:ClientId and Accounts:GitHub:ClientSecret are required outside development (this is {builder.Environment.EnvironmentName}).")
            .ValidateOnStart();

        var authentication = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie => AccountSessions.Configure(cookie, builder.Environment));

        // The antiforgery cookie is Secure in production too. (ASP.NET Core leaves it off by default.)
        builder.Services.Configure<AntiforgeryOptions>(antiforgery => antiforgery.Cookie.SecurePolicy = CookieSecurity.PolicyFor(builder.Environment));

        var github = builder.Configuration.GetSection($"{AccountOptions.SectionName}:GitHub").Get<GitHubAppOptions>();
        if (github is { IsConfigured: true })
        {
            authentication.AddOAuth(GitHubSignIn.Scheme, "GitHub", oauth =>
            {
                oauth.ClientId = github.ClientId!;
                oauth.ClientSecret = github.ClientSecret!;
                oauth.CallbackPath = "/signin-github";
                oauth.AuthorizationEndpoint = GitHubSignIn.AuthorizationEndpoint;
                oauth.TokenEndpoint = GitHubSignIn.TokenEndpoint;
                oauth.UserInformationEndpoint = GitHubSignIn.UserEndpoint;
                oauth.Scope.Add("user:email");

                // PKCE (#192): GitHub has accepted it for OAuth apps since July 2025, with S256 only, which is what ASP.NET Core
                // sends. The client secret is still required alongside it.
                oauth.UsePkce = true;
                oauth.Events.OnCreatingTicket = GitHubSignIn.OnCreatingTicketAsync;
            });
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdventPolicies.Admin, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AdminAuthorizationHandler>();
        builder.Services.AddScoped<ContributorSignIn>();
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }

    public static bool IsDevSignInEnabled(IHostEnvironment environment, AccountOptions options) =>
        environment.IsDevelopment() && options.DevSignIn;

    public static WebApplication MapAccounts(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<AccountOptions>>().Value;
        var devSignIn = IsDevSignInEnabled(app.Environment, options);

        app.MapGet("/signin", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            var target = ReturnUrl.Sanitize(returnUrl);

            if (await schemes.GetSchemeAsync(GitHubSignIn.Scheme) is not null)
            {
                return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [GitHubSignIn.Scheme]);
            }

            return devSignIn
                ? Results.Redirect($"{DevSignInPagePath}?returnUrl={Uri.EscapeDataString(target)}")
                : Results.Problem("Sign-in isn't configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        app.MapPost("/signout", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            // Minimal APIs only enforce antiforgery when they bind form data, so check the token explicitly.
            if (!await antiforgery.IsRequestValidAsync(http))
            {
                return Results.BadRequest();
            }

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("~/");
        });

        if (devSignIn)
        {
            // Signs in as any GitHub user, with a made-up verified email. Never mapped outside development.
            app.MapGet("/dev/signin", async (string login, long id, string? name, string? returnUrl, HttpContext http, ContributorSignIn contributors) =>
            {
                if (string.IsNullOrWhiteSpace(login) || id <= 0)
                {
                    return Results.BadRequest("A login and a positive GitHub user ID are required.");
                }

                var email = $"{login}@example.com";
                var contributorId = await contributors.RecordAsync(id, login, email, http.RequestAborted);
                var identity = new ClaimsIdentity(AdventClaims.For(id, login, email, contributorId, name), CookieAuthenticationDefaults.AuthenticationScheme);

                await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                return Results.LocalRedirect(ReturnUrl.Sanitize(returnUrl));
            });
        }

        return app;
    }
}
