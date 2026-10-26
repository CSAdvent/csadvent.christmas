using CSAdvent.Web.SeasonTesting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Auth;

/// <summary>
/// The sign-in cookie (#192): how long a sign-in lasts, and that it's only ever sent over HTTPS. Everyone stays signed in for
/// 14 days after their last visit, except organizers, whose cookie can do everything: theirs lasts 12 hours from when they
/// signed in, however busy they are.
/// </summary>
public static class AccountSessions
{
    public const string CookieName = "csadvent.auth";

    /// <summary>How long a sign-in lasts after the last visit, for everyone but the organizers.</summary>
    public static readonly TimeSpan Session = TimeSpan.FromDays(14);

    /// <summary>
    /// How long an organizer's sign-in lasts, from when they signed in. It isn't renewed by using the site. Signing in again
    /// is one click while they're signed in to GitHub.
    /// </summary>
    public static readonly TimeSpan OrganizerSession = TimeSpan.FromHours(12);

    public static void Configure(CookieAuthenticationOptions cookie, IHostEnvironment environment)
    {
        cookie.Cookie.Name = CookieName;
        cookie.Cookie.SecurePolicy = CookieSecurity.PolicyFor(environment);
        cookie.LoginPath = "/signin";
        cookie.AccessDeniedPath = "/access-denied";
        cookie.ExpireTimeSpan = Session;
        cookie.SlidingExpiration = true;
        cookie.Events.OnSigningIn = OnSigningInAsync;
        cookie.Events.OnValidatePrincipal = OnValidatePrincipalAsync;
    }

    /// <summary>Whether a sign-in is an organizer's: one that ends <see cref="OrganizerSession"/> after it began, and isn't renewed.</summary>
    public static bool IsOrganizerSession(AuthenticationProperties properties) =>
        properties is { AllowRefresh: false, IssuedUtc: { } issued, ExpiresUtc: { } expires } && expires - issued <= OrganizerSession;

    private static Task OnSigningInAsync(CookieSigningInContext context)
    {
        if (IsOrganizer(context.HttpContext, context.Principal))
        {
            var issued = context.Properties.IssuedUtc ?? context.Options.TimeProvider!.GetUtcNow();
            context.Properties.IssuedUtc = issued;
            context.Properties.ExpiresUtc = issued + OrganizerSession;
            context.Properties.AllowRefresh = false;
        }

        return Task.CompletedTask;
    }

    private static async Task OnValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } user)
        {
            return;
        }

        // Someone made an organizer while signed in, or signed in before organizers' sign-ins were shortened, has an everyday
        // 14-day sign-in. They sign in again, and get an organizer's.
        if (IsOrganizer(context.HttpContext, user) && !IsOrganizerSession(context.Properties))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        await TesterInvitations.ClaimForSignedInUserAsync(context.HttpContext, user);
    }

    private static bool IsOrganizer(HttpContext context, System.Security.Claims.ClaimsPrincipal? user) =>
        user is not null && AdventPolicies.IsOrganizer(user, context.RequestServices.GetRequiredService<IOptionsMonitor<AccountOptions>>().CurrentValue);
}

/// <summary>
/// Every cookie the site sets is marked Secure in production, whatever the request looked like, so a browser never sends it
/// over plain HTTP (#192). Development follows the request instead, because the integration tests and the <c>http</c> launch
/// profile run over plain HTTP, where a Secure cookie would never come back.
/// </summary>
public static class CookieSecurity
{
    public static CookieSecurePolicy PolicyFor(IHostEnvironment environment) =>
        environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    /// <summary>Whether a cookie set by hand on this request should be Secure.</summary>
    public static bool IsSecure(HttpContext context) =>
        PolicyFor(context.RequestServices.GetRequiredService<IHostEnvironment>()) == CookieSecurePolicy.Always || context.Request.IsHttps;
}
