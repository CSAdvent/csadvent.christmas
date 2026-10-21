using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Claims;

public sealed class ClaimEmailsTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas/" };

    private static ClaimEmailDetails Claim(int slotsPerDay = 2, string? link = "https://example.com/ada", string? comments = null) =>
        new(42, "Ada Lovelace", "ada@example.com", "ada-dev", link, comments, 2026, 3, 2, slotsPerDay);

    [Theory]
    [InlineData(2, "December 3, 2026 (post 2)")]
    [InlineData(1, "December 3, 2026")]
    public void TheSpotOnlyMentionsThePostWhenADayHasMoreThanOne(int slotsPerDay, string expected) =>
        Assert.Equal(expected, Claim(slotsPerDay).Spot);

    [Fact]
    public void TheContributorHearsTheirRequestArrived()
    {
        var email = ClaimEmails.Received(Claim(), Site);

        Assert.Equal("We received your C# Advent request for December 3", email.Subject);
        Assert.Equal("Thanks, Ada Lovelace!", email.Heading);
        Assert.Contains("December 3, 2026 (post 2)", Text(email));
    }

    [Fact]
    public void TheOrganizersGetTheDetailsAndALinkToReviewIt()
    {
        var email = ClaimEmails.NewRequest(Claim(comments: null), Site);

        Assert.Equal("New C# Advent request: Ada Lovelace for December 3", email.Subject);
        var details = Assert.Single(email.Body.OfType<DetailsBlock>());
        Assert.Equal(
            [("Name", "Ada Lovelace"), ("GitHub", "ada-dev"), ("Email", "ada@example.com"), ("Link", "https://example.com/ada"), ("Comments", "None")],
            details.Rows.Select(r => (r.Label, r.Value)));
        Assert.Equal("https://csadvent.christmas/manage/requests/42", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void TheApprovalExplainsWhatHappensNext()
    {
        var email = ClaimEmails.Approved(Claim(), Site);
        var text = Text(email);

        Assert.Equal("You're on the C# Advent calendar: December 3", email.Subject);
        Assert.Contains("December 3, 2026 (post 2) is yours.", text);
        Assert.Contains("Link to csadvent.christmas (https://csadvent.christmas) in your post", text);
        Assert.Contains("Keep it to yourself until December 3", text);
        Assert.Contains("reply to this email with them", text);
        Assert.Contains("terms (https://csadvent.christmas/terms)", text);
    }

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
