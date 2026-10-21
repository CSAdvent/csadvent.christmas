using CSAdvent.Web.Backups;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Backups;

public sealed class BackupEmailsTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };
    private static readonly BackupSpot Spot = new(2026, 12, 2, 2);

    [Fact]
    public void AsksForAMissingLinkWarnThatItIsNoGuarantee()
    {
        var email = BackupEmails.Ask("Grace", Spot, BackupReason.LinkMissing, "10:00 PM Eastern on December 11", "https://csadvent.christmas/backups/offers/7", Site);
        var text = Text(email);

        Assert.Equal("Can you fill in on C# Advent, December 12?", email.Subject);
        Assert.Contains("There's no guarantee yet", text);
        Assert.Contains("by 10:00 PM Eastern on December 11", text);
        Assert.Equal("https://csadvent.christmas/backups/offers/7", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void AsksForADroppedSpotSayItIsOpen()
    {
        var text = Text(BackupEmails.Ask("Grace", Spot, BackupReason.DroppedOut, "1:30 PM Eastern on November 20", "https://csadvent.christmas/backups/offers/7", Site));

        Assert.Contains("December 12, 2026 (post 2) had to drop out, so the spot is open", text);
        Assert.DoesNotContain("guarantee", text);
    }

    [Fact]
    public void TheOrganizersHearWhyASpotWasFilled()
    {
        Assert.Contains("which its author had dropped", Text(BackupEmails.SpotFilled("Grace", Spot, BackupReason.DroppedOut, Site)));
        Assert.Contains("didn't arrive before the reveal", Text(BackupEmails.SpotFilled("Grace", Spot, BackupReason.LinkMissing, Site)));
    }

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
