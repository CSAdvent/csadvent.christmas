using CSAdvent.Web.Auth;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.SeasonTesting;

/// <summary>
/// Who sees what's going on in a season being tested: its testers and the organizers. To everyone else the season looks
/// untouched, still waiting for its sign-ups to open: no calendar, no spots to claim, no entries, cards or feed items.
/// Background work (reminders, backups) has no signed-in user and sees everything.
/// </summary>
public sealed class SeasonAccess(TestSeasons testSeasons, IHttpContextAccessor httpContextAccessor, IOptionsMonitor<AccountOptions> accounts)
{
    /// <summary>Whether the current user can see what's happening in the season: always, unless it's being tested.</summary>
    public bool CanSee(int year) => testSeasons.Find(year) is not { } season || IsTester(season);

    /// <summary>The earliest season being tested that the current user is testing, for the banner on every page.</summary>
    public TestSeason? Testing => testSeasons.All.Where(IsTester).MinBy(s => s.Year);

    private bool IsTester(TestSeason season)
    {
        if (httpContextAccessor.HttpContext?.User is not { } user)
        {
            return true;
        }

        return AdventPolicies.IsOrganizer(user, accounts.CurrentValue)
            || (user.Identity?.IsAuthenticated == true && user.Identity.Name is { } login && season.Testers.Contains(login));
    }
}
