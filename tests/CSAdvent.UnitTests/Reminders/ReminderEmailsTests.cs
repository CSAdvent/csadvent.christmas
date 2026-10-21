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
        Assert.EndsWith("Stop these emails: https://csadvent.christmas/emails/daily/stop?token=abc\n", text);
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

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
