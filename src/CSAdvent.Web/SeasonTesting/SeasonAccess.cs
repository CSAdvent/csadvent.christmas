using CSAdvent.Web.Auth;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// Who sees the hidden test season (<see cref="Season.TestYear"/>): its testers and the organizers. To everyone else it
/// doesn't exist: its calendar, spots, entries, cards and backup list are 404s. Testers see the real site as usual, and switch
/// to test mode (<see cref="TestModeCookie"/>) to get the test calendar on the home page. Background work (reminders,
/// backups) has no signed-in user and sees everything.
/// </summary>
public sealed class SeasonAccess(TestSeasons testSeasons, IHttpContextAccessor httpContextAccessor, IOptionsMonitor<AccountOptions> accounts)
{
    public const string TestModeCookie = "csadvent-test-mode";

    /// <summary>Whether the current user can see the season: every real season, and the test season if they're a tester.</summary>
    public bool CanSee(int year) => year != Season.TestYear || IsTester;

    /// <summary>An organizer, or invited to the test season. Without a request (background work), always.</summary>
    public bool IsTester
    {
        get
        {
            if (httpContextAccessor.HttpContext?.User is not { } user)
            {
                return true;
            }

            return AdventPolicies.IsOrganizer(user, accounts.CurrentValue)
                || (user.Identity?.IsAuthenticated == true && user.Identity.Name is { } login && testSeasons.Current?.Testers.Contains(login) == true);
        }
    }

    public bool IsOrganizer => httpContextAccessor.HttpContext?.User is { } user && AdventPolicies.IsOrganizer(user, accounts.CurrentValue);

    /// <summary>The test season, for a tester; null for everyone else, or before an organizer has created it.</summary>
    public TestSeason? TestSeason => IsTester ? testSeasons.Current : null;

    /// <summary>A tester has switched to test mode, so the home page and the archive show the test season.</summary>
    public bool InTestMode =>
        TestSeason is not null && httpContextAccessor.HttpContext?.Request.Cookies[TestModeCookie] == "on";
}
