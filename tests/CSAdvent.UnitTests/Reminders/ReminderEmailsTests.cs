using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using CSAdvent.Web.Reminders;

namespace CSAdvent.UnitTests.Reminders;

public sealed class ReminderEmailsTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static PostLinkReminder Reminder(int daysBefore) => new("Ada Lovelace", 2026, 10, 2, 2, daysBefore);

    [Theory]
    [InlineData(7, "Your C# Advent post is due in 7 days (December 10)", "Your day is 7 days away!")]
    [InlineData(1, "Your C# Advent post is due tomorrow (December 10)", "Your day is tomorrow!")]
    public void PostLinkRemindersSayHowSoonTheDayIs(int daysBefore, string subject, string heading)
    {
        var email = ReminderEmails.PostLinkMissing(Reminder(daysBefore), Site);

        Assert.Equal((subject, heading), (email.Subject, email.Heading));
        Assert.Contains("December 10, 2026 (post 2), and we don't have the link to your post yet.", Text(email));
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.DoesNotContain("reply", Text(email), StringComparison.OrdinalIgnoreCase);
        Assert.Null(email.UnsubscribeUrl);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(5, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    [InlineData(1, true)]
    public void OnlyTheLastRemindersWarnAboutBackupPosters(int daysBefore, bool warns) =>
        Assert.Equal(warns, Text(ReminderEmails.PostLinkMissing(Reminder(daysBefore), Site)).Contains("the backup takes your spot"));

    [Theory]
    [InlineData(10, 0, "10 AM")]
    [InlineData(14, 0, "2 PM")]
    [InlineData(14, 30, "2:30 PM")]
    public void TheDayOfReminderGivesTheAnnouncementAsTheDeadline(int hour, int minute, string announced)
    {
        var email = ReminderEmails.PostLinkDueToday(new DayOfReminder("Ada Lovelace", 2026, 10, 2, 2, new TimeOnly(hour, minute)), Site);

        Assert.Equal($"Today's your C# Advent day: add your post's link by {announced} Eastern", email.Subject);
        Assert.Contains("today is your C# Advent day, December 10, 2026 (post 2)", Text(email));
        Assert.Contains($"at {announced} Eastern", Text(email));
        Assert.Contains("a backup poster can take your spot", Text(email));
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Theory]
    [InlineData(6, 59, 10, false)]
    [InlineData(7, 0, 10, true)]
    [InlineData(9, 59, 10, true)]
    [InlineData(10, 0, 10, false)]
    [InlineData(10, 59, 14, false)]
    [InlineData(11, 0, 14, true)]
    [InlineData(0, 30, 1, true)]
    public void TheDayOfReminderGoesOutInTheHoursBeforeTheAnnouncement(int hour, int minute, int announcedHour, bool due) =>
        Assert.Equal(due, ReminderService.IsDayOfReminderDue(new TimeOnly(hour, minute), new TimeOnly(announcedHour, 0), TimeSpan.FromHours(3)));

    [Fact]
    public void TheDailyEmailListsTodaysPostsAndCanBeTurnedOff()
    {
        var email = ReminderEmails.Daily(
            new DailyShare(2026, 3, [new SharedPost("Span<T> tricks", "https://example.com/span", "Grace Hopper"), new SharedPost("Records", "https://example.com/records", "Alan Turing")], "Ada Lovelace", RecipientIsAuthorToday: false),
            Site,
            "https://csadvent.christmas/emails/daily/stop?token=abc");
        var text = Text(email);

        Assert.Equal("Day 3 of C# Advent: 2 new posts to share", email.Subject);
        Assert.Contains("- Span<T> tricks (https://example.com/span) by Grace Hopper\n- Records (https://example.com/records) by Alan Turing\n", text);
        Assert.DoesNotContain("is yours", text);
        Assert.Equal("https://csadvent.christmas/emails/daily/stop?token=abc", email.UnsubscribeUrl);
        Assert.EndsWith("Unsubscribe: https://csadvent.christmas/emails/daily/stop?token=abc\n", text);
    }

    [Fact]
    public void ADaysAuthorsAreThankedAndASinglePostIsNamedInTheSubject()
    {
        var email = ReminderEmails.Daily(
            new DailyShare(2026, 3, [new SharedPost("Span<T> tricks", "https://example.com/span", "Ada Lovelace")], "Ada Lovelace", RecipientIsAuthorToday: true),
            Site,
            "https://csadvent.christmas/emails/daily/stop?token=abc");

        Assert.Equal("Day 3 of C# Advent: Span<T> tricks", email.Subject);
        Assert.Contains("One of today's posts is yours. Thank you for taking part!", Text(email));
    }

    [Fact]
    public void ReadersGetTheSamePostsWithTheirOwnReasonAndLink()
    {
        var email = ReminderEmails.DailyForReaders(
            3,
            [new SharedPost("Span<T> tricks", "https://example.com/span", "Grace Hopper"), new SharedPost("Records", "https://example.com/records", "Alan Turing")],
            Site,
            "https://csadvent.christmas/emails/announcements/daily-posts/stop?token=abc");
        var text = Text(email);

        Assert.Equal("Day 3 of C# Advent: 2 new posts to share", email.Subject);
        Assert.Contains("Hi! Here's what's new on the C# Advent calendar today:", text);
        Assert.Contains("- Span<T> tricks (https://example.com/span) by Grace Hopper\n- Records (https://example.com/records) by Alan Turing\n", text);
        Assert.Contains("you asked for C# Advent's daily posts by email", text);
        Assert.DoesNotContain("spot", text);
        Assert.False(email.Transactional);
        Assert.Equal("https://csadvent.christmas/emails/announcements/daily-posts/stop?token=abc", email.UnsubscribeUrl);
    }

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
