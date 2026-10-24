using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Announcements;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
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

    [Fact]
    public void AnAnnouncementHasItsParagraphsButtonAndTheSubscribersOwnUnsubscribeLink()
    {
        var announcement = new Announcement
        {
            Subject = "The calendar is full",
            Message = "Every spot is taken.\nThank you!\n\n\nJoin the backup list.",
            ButtonLabel = "Join the backup list",
            ButtonUrl = "https://csadvent.christmas/backups/2026",
            CreatedBy = "CalvinAllen",
        };

        var email = AnnouncementService.Email(announcement, "https://csadvent.christmas/emails/announcements/stop?token=abc", Site);

        Assert.Equal(("The calendar is full", "The calendar is full"), (email.Subject, email.Heading));
        Assert.Equal("https://csadvent.christmas/emails/announcements/stop?token=abc", email.UnsubscribeUrl);
        Assert.Equal("https://csadvent.christmas/backups/2026", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.Equal(["Every spot is taken. Thank you!", "Join the backup list."], AnnouncementService.Paragraphs(announcement.Message));
        Assert.Equal("Every spot is taken. Thank you!", email.Preview);
    }

    [Fact]
    public void TheSignUpsEmailNamesTheSeasonAndLinksToTheCalendar()
    {
        var form = AnnouncementService.SignUpsOpen(2026, "https://csadvent.christmas");

        Assert.Equal("C# Advent 2026 sign-ups are open", form.Subject);
        Assert.Equal(("Claim your spot", "https://csadvent.christmas/"), (form.ButtonLabel, form.ButtonUrl));
        Assert.Empty(Errors(form));
    }

    [Fact]
    public void AButtonNeedsBothALabelAndALink() =>
        Assert.Equal(["Give the button both a label and a link, or neither."], Errors(new AnnouncementForm { Subject = "Hi", Message = "Hello", ButtonLabel = "Go" }));

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
