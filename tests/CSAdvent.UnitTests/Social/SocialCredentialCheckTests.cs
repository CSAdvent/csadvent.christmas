using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using CSAdvent.Web.Social.Posting;

namespace CSAdvent.UnitTests.Social;

public sealed class SocialCredentialCheckTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    [Fact]
    public void TheAlertSaysWhichPlatformWhatItSaidAndWhatToDo()
    {
        var email = SocialCredentialCheck.CredentialsFailing(SocialPlatform.Bluesky, "401 Unauthorized:\nInvalid identifier or password", Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("Automatic posting to Bluesky has stopped working", email.Subject);
        Assert.Contains("automatic posts to Bluesky will fail until they're fixed", text);
        Assert.Equal(new DetailRow("What Bluesky said", "401 Unauthorized:Invalid identifier or password"), Assert.Single(Assert.Single(email.Body.OfType<DetailsBlock>()).Rows));
        Assert.Equal("https://csadvent.christmas/manage/social", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void TheCheckRunsEarlyEnoughToFixBeforeTheDaysPosts() =>
        Assert.True(new SocialPostingOptions().CheckAt < new TimeOnly(10, 0));
}
