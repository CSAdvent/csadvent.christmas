using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Contributors;

public sealed class PostClaimTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas/" };

    private static PostClaimEmailDetails Request(string? name = "Ada Lovelace", string? note = "My old blog name was A. Byron.", int slotsPerDay = 2) =>
        new(7, "ada-dev", name, note, 2019, 12, 2, slotsPerDay, "Pattern matching all the way down", "A. Byron");

    [Theory]
    [InlineData("Ada Lovelace", "Ada Lovelace (ada-dev)")]
    [InlineData("ADA-DEV", "ada-dev")]
    [InlineData(null, "ada-dev")]
    public void TheRequesterIsNamedWithTheirLogin(string? name, string expected) => Assert.Equal(expected, Request(name).Who);

    [Theory]
    [InlineData(2, "December 12, 2019, post 2")]
    [InlineData(1, "December 12, 2019")]
    public void TheSpotOnlyMentionsThePostWhenADayHadMoreThanOne(int slotsPerDay, string expected) =>
        Assert.Equal(expected, Request(slotsPerDay: slotsPerDay).Spot);

    [Fact]
    public void TheOrganizersGetEverythingTheyNeedToCheckIt()
    {
        var email = PostClaimEmails.NewRequest(Request(note: null), Site);

        Assert.Equal("Is this their post? ada-dev asked for a C# Advent 2019 post", email.Subject);
        Assert.Equal(
            [
                ("Post", "Pattern matching all the way down"),
                ("Author on the post", "A. Byron"),
                ("When", "December 12, 2019, post 2"),
                ("Asked by", "Ada Lovelace (ada-dev), https://github.com/ada-dev"),
                ("Their note", "None"),
            ],
            Assert.Single(email.Body.OfType<DetailsBlock>()).Rows.Select(r => (r.Label, r.Value)));
        Assert.Equal("https://csadvent.christmas/manage/authors/requests/7", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void AnApprovalLinksThePostAndTheirHistory()
    {
        var email = PostClaimEmails.Approved(Request(), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("Your C# Advent 2019 post is on your account", email.Subject);
        Assert.Contains("https://csadvent.christmas/2019/day/12/2", text);
        Assert.Contains("from December 12, 2019, post 2 is now linked to your GitHub account", text);
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void ADeclineInvitesThemToReplyWithMore()
    {
        var text = EmailText.Render(PostClaimEmails.Declined(Request(), Site), Site.BaseUrl);

        Assert.Contains("We couldn't confirm that “Pattern matching all the way down” from December 12, 2019, post 2 is yours", text);
        Assert.Contains("reply to this email", text);
    }

    [Fact]
    public void SubjectsStayOnOneLine()
    {
        var email = PostClaimEmails.Approved(Request() with { Title = "Two\nlines" }, Site);

        Assert.DoesNotContain('\n', email.Preview);
        Assert.DoesNotContain('\n', email.Subject);
    }

    [Theory]
    [InlineData("Ada", "Ada")]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"back\slash", @"back\\slash")]
    public void SearchesMatchOnlyWhatWasTyped(string search, string pattern) => Assert.Equal(pattern, PostClaimService.EscapeLike(search));

    [Fact]
    public void AskingNeedsThemToSayTheyWroteIt()
    {
        Assert.Equal(["Tick the box to confirm you wrote this post."], Errors(new PostClaimForm()));
        Assert.Empty(Errors(new PostClaimForm { IWroteIt = true }));
        Assert.Equal(["Keep your note to 1,000 characters or fewer."], Errors(new PostClaimForm { IWroteIt = true, Note = new string('a', 1001) }));
    }

    [Fact]
    public void TheClaimPageIsUnderMySpots() => Assert.Equal("my-spots/claim/2019/12/2", PostClaimLinks.Claim(2019, 12, 2));

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
