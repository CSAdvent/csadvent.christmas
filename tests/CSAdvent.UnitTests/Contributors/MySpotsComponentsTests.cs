using Bunit;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Contributors;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Manage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Contributors;

/// <summary>What My spots shows about each spot and the backup list (#175).</summary>
public sealed class MySpotsComponentsTests : BunitContext
{
    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);

    // December 6, post 2: backups asked from 6 PM on the 5th, the blurb checked from midnight on the 5th, revealed at midnight,
    // and announced at 2 PM.
    private static readonly SpotDeadlines December6 = new(
        AskBackupsFrom: new(2026, 12, 5, 18, 0, 0, Eastern),
        CheckBlurbFrom: new(2026, 12, 5, 0, 0, 0, Eastern),
        RevealsAt: new(2026, 12, 6, 0, 0, 0, Eastern),
        AnnouncedAt: new(2026, 12, 6, 14, 0, 0, Eastern));

    private FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 12, 1, 12, 0, 0, Eastern));

    public MySpotsComponentsTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), Clock));

    private static MySpot Spot(string? postUrl, bool isDayOver = false) =>
        new(1, 2026, 6, 2, 2, postUrl is null ? null : "Spans", postUrl, postUrl is null ? EntryStatus.ClaimedWithoutPost : EntryStatus.Ready, isDayOver, IsRevealed: false, PostTraffic.None)
        {
            Deadlines = December6,
        };

    private string[] Deadlines(MySpot spot, bool checksBlurb = true) =>
        [.. Render<SpotDeadlineList>(p => p.Add(c => c.Spot, spot).Add(c => c.ChecksBlurb, checksBlurb)).FindAll("li").Select(li => string.Join(' ', li.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    [Fact]
    public void ASpotWithoutItsLinkSaysWhenBackupsAreAsked()
    {
        Assert.Equal(
            [
                "Add your post's link by 6:00 PM Eastern, December 5. Then we start asking backup posters to stand by for your spot.",
                "We start checking your post for the blurb at midnight Eastern, December 5.",
                "Revealed at midnight Eastern, December 6.",
                "Announced at 2:00 PM Eastern, December 6. Your post needs the blurb by then, or a backup poster may take your spot.",
            ],
            Deadlines(Spot(postUrl: null)));
    }

    [Fact]
    public void OnceBackupsAreAskedTheLinkIsDueAtTheReveal()
    {
        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 5, 20, 0, 0, Eastern));

        Assert.Equal(
            [
                "Add your post's link before midnight Eastern, December 6. We're asking backup posters, and one who says yes takes your spot then.",
                "Revealed at midnight Eastern, December 6.",
                "Announced at 2:00 PM Eastern, December 6. Your post needs the blurb by then, or a backup poster may take your spot.",
            ],
            Deadlines(Spot(postUrl: null)));
    }

    [Fact]
    public void OnItsDayAPostWithoutALinkIsDueNow()
    {
        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 6, 9, 0, 0, Eastern));

        Assert.Equal(
            ["Add your post's link now: backup posters have been asked, and one who says yes takes your spot straight away."],
            Deadlines(Spot(postUrl: null)));
    }

    [Fact]
    public void APostWithItsLinkOnlyHasItsRevealAndAnnouncementLeft()
    {
        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 6, 9, 0, 0, Eastern));

        Assert.Equal(["Announced at 2:00 PM Eastern, December 6."], Deadlines(Spot("https://example.com/spans"), checksBlurb: false));
        Assert.Empty(Deadlines(Spot("https://example.com/spans", isDayOver: true)));
    }

    [Fact]
    public void TheBlurbComesInOneFormatAtATime()
    {
        var box = Render<SuggestedBlurbBox>(p => p.Add(c => c.Year, 2026).Add(c => c.SlotsPerDay, 2));

        Assert.Equal(["text", "markdown", "html"], box.FindAll("input[type=radio]").Select(r => r.GetAttribute("value")));
        Assert.True(box.Find("input[value=text]").HasAttribute("checked"));
        Assert.All(box.FindAll("input[type=radio]"), r => Assert.Equal("blurb-format-2026", r.GetAttribute("name")));

        // Each format has its box and Copy button, and only the text one shows until another is picked.
        Assert.Equal(["blurb-2026-text", "blurb-2026-markdown", "blurb-2026-html"], box.FindAll("pre").Select(p => p.Id));
        Assert.Equal(["blurb-2026-text", "blurb-2026-markdown", "blurb-2026-html"], box.FindAll("button[data-copy]").Select(b => b.GetAttribute("data-copy")));
        Assert.Contains("[2026 C# Advent calendar](https://csadvent.christmas)", box.Find("#blurb-2026-markdown").TextContent);
        Assert.StartsWith("block ", box.Find("[data-format=text]").ClassName);
        Assert.StartsWith("hidden ", box.Find("[data-format=html]").ClassName);
    }

    [Fact]
    public void BackupAsksLinkToTheirPage()
    {
        var standing = new MyBackups(2026, BackupSignupAvailability.Open, new BackupMembership(2, null), [new MyBackupOffer(7, 2026, 9, 1, 2, StandingBy: false), new MyBackupOffer(8, 2026, 12, 2, 2, StandingBy: true)]);

        var box = Render<BackupStanding>(p => p.Add(c => c.Standing, standing));

        Assert.Contains("We've asked you to fill December 9 · Post 1.", box.Markup);
        Assert.Contains("You're standing by for December 12 · Post 2.", box.Markup);
        Assert.Equal(["backups/offers/7", "backups/offers/8", "backups/2026"], box.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.Contains("You're number 2 on the backup list.", box.Markup);
        Assert.Equal("backups-2026", box.Find("div").Id);
    }

    [Fact]
    public void ContributorsOffTheListAreInvitedToJoinIt()
    {
        var box = Render<BackupStanding>(p => p.Add(c => c.Standing, new MyBackups(2026, BackupSignupAvailability.Open, null, [])));

        Assert.Equal("Join the backup list", box.Find("a[href='backups/2026']").TextContent);
    }
}
