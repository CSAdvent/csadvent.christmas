using System.Collections.Frozen;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Manage;
using CSAdvent.Web.SeasonTesting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static CSAdvent.UnitTests.Manage.TodayFixtures;

namespace CSAdvent.UnitTests.Manage;

/// <summary>The Today panel's days and statuses, and the morning digest (#171).</summary>
public sealed class TodayTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static readonly DateTimeOffset Reveal = new(2026, 12, 6, 5, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Announcement = new(2026, 12, 6, 15, 0, 0, TimeSpan.Zero);

    private static AdventSchedule Schedule(TestSeasons? testSeasons = null) =>
        new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(Now), testSeasons);

    [Theory]
    [InlineData(11, 29, new int[0])]
    [InlineData(11, 30, new[] { 1 })]
    [InlineData(12, 1, new[] { 1, 2 })]
    [InlineData(12, 24, new[] { 24, 25 })]
    [InlineData(12, 25, new[] { 25 })]
    [InlineData(12, 26, new int[0])]
    public void ARealSeasonHasTodayAndTomorrowFromNovember30ToChristmas(int month, int day, int[] days) =>
        Assert.Equal(days, TodayService.DaysOf(Schedule(), 2026, new DateOnly(2026, month, day)).Select(d => d.Day));

    [Fact]
    public void TodayComesBeforeTomorrow()
    {
        Assert.Equal([(6, true), (7, false)], TodayService.DaysOf(Schedule(), 2026, new DateOnly(2026, 12, 6)));
        Assert.Equal([(1, false)], TodayService.DaysOf(Schedule(), 2026, new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void TheTestSeasonsDaysFollowItsRehearsal()
    {
        // The rehearsal's December 1 is October 20, so October 25 is its December 6, whatever the real date.
        var testSeasons = new TestSeasons(null!);
        testSeasons.Replace([new TestSeason(Season.TestYear, new DateOnly(2026, 10, 20), FrozenSet<string>.Empty)]);

        Assert.Equal([(6, true), (7, false)], TodayService.DaysOf(Schedule(testSeasons), Season.TestYear, new DateOnly(2026, 10, 25)));
        Assert.Empty(TodayService.DaysOf(Schedule(testSeasons), Season.TestYear, new DateOnly(2026, 12, 6)));
    }

    [Theory]
    [InlineData(SocialPostStatus.Posted, TodayPostingStatus.Posted)]
    [InlineData(SocialPostStatus.Posting, TodayPostingStatus.Posting)]
    [InlineData(SocialPostStatus.Failed, TodayPostingStatus.Failed)]
    [InlineData(SocialPostStatus.Unconfirmed, TodayPostingStatus.Unconfirmed)]
    public void WhatsRecordedComesFirst(SocialPostStatus recorded, TodayPostingStatus status)
    {
        var post = new SocialPost { SlotId = 1, Platform = SocialPlatform.Facebook, Status = recorded, MarkedBy = "automation" };

        Assert.Equal(status, TodayService.StatusOf(post, automatic: true, canAnnounce: false, Now, Reveal, Announcement));
        Assert.Equal(status, TodayService.StatusOf(post, automatic: false, canAnnounce: true, Now, Reveal, Announcement));
    }

    [Fact]
    public void AutomaticPlatformsWaitForTheirTimeAndTheBlurbCheck()
    {
        Assert.Equal(TodayPostingStatus.Scheduled, TodayService.StatusOf(null, automatic: true, canAnnounce: true, Now, Reveal, Announcement));
        Assert.Equal(TodayPostingStatus.Due, TodayService.StatusOf(null, automatic: true, canAnnounce: true, Announcement, Reveal, Announcement));
        Assert.Equal(TodayPostingStatus.HeldForBlurb, TodayService.StatusOf(null, automatic: true, canAnnounce: false, Announcement, Reveal, Announcement));
    }

    [Fact]
    public void OtherPlatformsArePostedByHandAfterTheReveal()
    {
        Assert.Equal(TodayPostingStatus.ByHandLater, TodayService.StatusOf(null, automatic: false, canAnnounce: true, Reveal.AddMinutes(-1), Reveal, Announcement));
        Assert.Equal(TodayPostingStatus.ToPost, TodayService.StatusOf(null, automatic: false, canAnnounce: false, Reveal, Reveal, Announcement));
    }

    [Fact]
    public void LabelsSayWhatHappensNext()
    {
        var tenAm = new DateTimeOffset(2026, 12, 6, 15, 0, 0, TimeSpan.Zero);
        var halfPastTwo = new DateTimeOffset(2026, 12, 6, 19, 30, 0, TimeSpan.Zero);

        Assert.Equal("Automatic at 10 AM", TodayLabels.Posting(new(SocialPlatform.Bluesky, true, TodayPostingStatus.Scheduled), tenAm, Eastern));
        Assert.Equal("Automatic at 2:30 PM", TodayLabels.Posting(new(SocialPlatform.Bluesky, true, TodayPostingStatus.Scheduled), halfPastTwo, Eastern));
        Assert.Equal("Posted automatically", TodayLabels.Posting(new(SocialPlatform.Bluesky, true, TodayPostingStatus.Posted, "automation"), tenAm, Eastern));
        Assert.Equal("Posted by CalvinAllen", TodayLabels.Posting(new(SocialPlatform.LinkedIn, false, TodayPostingStatus.Posted, "CalvinAllen"), tenAm, Eastern));
        Assert.Equal("Did it go out?", TodayLabels.Posting(new(SocialPlatform.X, true, TodayPostingStatus.Unconfirmed), tenAm, Eastern));
        Assert.Equal("To post by hand", TodayLabels.Posting(new(SocialPlatform.Reddit, false, TodayPostingStatus.ToPost), tenAm, Eastern));
        Assert.Equal("10:00 PM Eastern on December 6", TodayLabels.Eastern(new DateTimeOffset(2026, 12, 7, 3, 0, 0, TimeSpan.Zero), Eastern));
    }

    [Fact]
    public void TheDigestGoesToTheOrganizersAboutToday()
    {
        var email = MorningDigest.Email(SampleReport(SampleSeason()), SampleSeason(), Site);

        Assert.Equal("morning-digest", email.Kind);
        Assert.Equal("C# Advent 2026, December 6: the morning digest", email.Subject);
        Assert.Equal("December 6 at a glance", email.Heading);
        Assert.Equal("2 spots need attention, 1 post didn't go out, 1 email failed.", email.Preview);
        Assert.False(email.Transactional);
        Assert.Equal("You're getting this email because you're a C# Advent organizer.", email.Reason);
        Assert.Equal("https://csadvent.christmas/manage", email.Body.OfType<ButtonBlock>().Single().Url);
    }

    [Fact]
    public void TheDigestSaysWhatThePanelSays()
    {
        var text = EmailText.Render(MorningDigest.Email(SampleReport(SampleSeason()), SampleSeason(), Site), Site.BaseUrl);

        Assert.Contains("Today, December 6:", text);
        Assert.Contains(
            "- Post 1: Ada Lovelace, Span<T> all the way down (https://example.com/span). Ready. Blurb passed. Bluesky: posted automatically; " +
            "Mastodon: automatic at 10 AM; Facebook: failed; LinkedIn: to post by hand. Open (https://csadvent.christmas/manage/entries/41)",
            text);
        Assert.Contains("- Needs attention: Post 2: Grace Hopper. No post yet. Open (https://csadvent.christmas/manage/entries/42)", text);
        Assert.Contains("Tomorrow, December 7:", text);
        Assert.Contains("- Needs attention: Post 2: Open (dropped out).", text);
        Assert.Contains("- December 7, post 2: Barbara Liskov has been asked, and has until 11:00 AM Eastern on December 6 to answer.", text);
        Assert.Contains("- December 5, post 2 on X: did it go out? (https://csadvent.christmas/manage/social?year=2026&day=5) X didn't answer in time.", text);
        Assert.Contains("1 email the queue gave up on: Today's posts (1). Retry or discard them (https://csadvent.christmas/manage/emails/queue).", text);
        Assert.Contains("Requests waiting: 2", text);
        Assert.Contains("Emails waiting to go: 12", text);
    }

    [Fact]
    public void TheDigestNeverNamesAPostBeforeItsRevealOrGivesAnAddress()
    {
        var email = MorningDigest.Email(SampleReport(SampleSeason()), SampleSeason(), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        // Tomorrow's post is in, and that's all it says.
        Assert.Contains("- Post 1: Linus Torvalds. Ready. Blurb not checked yet. LinkedIn: by hand, after the reveal.", text);
        Assert.DoesNotContain(HiddenTitle, text);
        Assert.DoesNotContain(HiddenUrl, text);

        // The organizer pages have the addresses; the inbox doesn't need them.
        Assert.DoesNotContain("@example.com", text);
    }

    [Fact]
    public void TheTestSeasonsDigestSaysSoAndOpensItsOwnOverview()
    {
        var season = SampleSeason(year: Season.TestYear);
        var email = MorningDigest.Email(SampleReport(season), season, Site);

        Assert.Equal("C# Advent 9000, December 6: the morning digest", email.Subject);
        Assert.Equal("December 6 at a glance (test season)", email.Heading);
        Assert.Equal("https://csadvent.christmas/manage?year=9000", email.Body.OfType<ButtonBlock>().Single().Url);
        Assert.Contains("the test season (C# Advent 9000) on December 6 of its rehearsal", EmailText.Render(email, Site.BaseUrl));
    }

    [Fact]
    public void AQuietMorningSaysNothingNeedsThem()
    {
        var season = SampleSeason(backups: [], failed: []);
        var quiet = season with { Days = [.. season.Days.Select(d => d with { Spots = [.. d.Spots.Where(s => !s.NeedsAttention)] })] };

        Assert.Equal("Nothing needs you this morning.", MorningDigest.Preview(SampleReport(quiet, failedEmails: []), quiet));
    }

    [Fact]
    public void TheCatalogsSampleIsTheDigest()
    {
        var entry = EmailCatalog.Find("morning-digest")!;

        Assert.True(entry.IsForOrganizers);
        Assert.Equal("morning-digest", entry.Sample(Site).Kind);
    }
}
