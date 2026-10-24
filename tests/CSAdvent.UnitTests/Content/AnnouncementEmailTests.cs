using CSAdvent.Web.Announcements;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Content;

public sealed class AnnouncementEmailTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    [Fact]
    public void TheConfirmationLinksToConfirmAndSaysWhatHappensOtherwise()
    {
        var email = AnnouncementSubscriptions.Confirmation("https://csadvent.christmas/emails/announcements/confirm?token=abc", Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("Confirm your C# Advent announcements", email.Subject);
        Assert.Equal("https://csadvent.christmas/emails/announcements/confirm?token=abc", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.Contains("we delete the address within a week", text);
        Assert.Null(email.UnsubscribeUrl);
    }
}
