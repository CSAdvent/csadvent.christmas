using Bunit;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using CSAdvent.Web.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Manage;

public sealed class SpotBackupOffersTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 12, 5, 20, 0, 0, TimeSpan.FromHours(-5));

    public SpotBackupOffersTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void ShowsWhoWasAskedWhenAndTheirAnswer()
    {
        SpotBackupOffer[] offers =
        [
            new("Dee", "dee-gh", "dee@example.com", Now.AddHours(-6), Now.AddHours(-2), BackupOfferStatus.Declined, Now.AddHours(-5), null, null),
            new("Eve", null, "eve@example.com", Now.AddHours(-2), Now.AddHours(2), BackupOfferStatus.Accepted, Now.AddHours(-1), "A backup post", "https://example.com/eve"),
            new("Fay", null, "fay@example.com", Now.AddMinutes(-30), Now.AddHours(3), BackupOfferStatus.Asked, null, null, null),
        ];

        var panel = Render<SpotBackupOffers>(p => p.Add(c => c.Offers, offers).Add(c => c.Year, 2026).Add(c => c.Now, Now));

        var items = panel.FindAll("li");
        Assert.Equal(3, items.Count);
        Assert.Contains("dee-gh", items[0].TextContent);
        Assert.Contains("Declined", items[0].TextContent);
        Assert.Contains("Asked Dec 5, 2026, 2:00 PM Eastern.", items[0].TextContent);
        Assert.Contains("Answered Dec 5, 2026, 3:00 PM Eastern. They said not this time.", items[0].TextContent);
        Assert.Equal("mailto:dee@example.com", items[0].QuerySelector("a")!.GetAttribute("href"));

        Assert.Contains("Standing by", items[1].TextContent);
        Assert.Equal("https://example.com/eve", items[1].QuerySelector("a[target='_blank']")!.GetAttribute("href"));
        Assert.Contains("A backup post", items[1].TextContent);

        Assert.Contains("No answer yet. If there's still none at Dec 5, 2026, 11:00 PM Eastern, the next backup is asked too.", items[2].TextContent);
    }

    [Fact]
    public void SaysNobodyHasBeenAskedUntilTheDayIsOver()
    {
        var waiting = Render<SpotBackupOffers>(p => p.Add(c => c.Offers, []).Add(c => c.Year, 2026).Add(c => c.Now, Now));
        Assert.Contains("Nobody yet.", waiting.Markup);
        Assert.Equal("manage/backups?year=2026", waiting.Find("a").GetAttribute("href"));

        // An archive spot that never needed one says nothing.
        var history = Render<SpotBackupOffers>(p => p.Add(c => c.Offers, []).Add(c => c.Year, 2020).Add(c => c.IsDayOver, true).Add(c => c.Now, Now));
        Assert.Empty(history.Markup.Trim());
    }
}
