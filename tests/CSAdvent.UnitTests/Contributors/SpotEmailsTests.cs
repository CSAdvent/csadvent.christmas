using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Contributors;

public sealed class SpotEmailsTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas/" };

    [Fact]
    public void TheOrganizersSeeWhatARevealedPostWasSoTheyCanPutItBack()
    {
        var email = SpotEmails.PostChanged(new PostChangeDetails(42, "Ada Lovelace", 2026, 6, 2, 2, "Patterns", "Patterns, revisited", null, null), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("Ada Lovelace changed the title of their post for December 6", email.Subject);
        Assert.Contains("their post for December 6, 2026 (post 2) on My spots, after it was revealed", text);
        Assert.Contains("Was: Patterns", text);
        Assert.Contains("Now: Patterns, revisited", text);
        Assert.DoesNotContain("Summary", text);
        Assert.Equal("https://csadvent.christmas/manage/entries/42", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void ANewSummarySaysWhatItWas()
    {
        var email = SpotEmails.PostChanged(new PostChangeDetails(42, "Ada\nLovelace", 2026, 6, 1, 1, "Patterns", "Patterns", null, "Switch expressions and more."), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("AdaLovelace changed the summary of their post for December 6", email.Subject);
        Assert.Contains("Was: none", text);
        Assert.Contains("Now: Switch expressions and more.", text);
        Assert.DoesNotContain("Title", text);
        Assert.DoesNotContain("(post 1)", text);
    }
}
