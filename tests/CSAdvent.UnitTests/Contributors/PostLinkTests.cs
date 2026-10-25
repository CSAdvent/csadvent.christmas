using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Contributors;

public sealed class PostLinkTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas/" };

    private static readonly LinkedPost Records = new(11, 2021, 8, 1, 2, "Records, revisited", "https://ada.blogspot.com/records", 3);

    private static readonly LinkedPost Patterns = new(12, 2019, 12, 2, 2, "Pattern matching all the way down", "https://ada.blogspot.com/patterns", 4);

    private static PostLinkEmailDetails Request(string? name = "Ada Lovelace", string? note = "I moved my blog.", int changes = 2) =>
        new(9, "ada-dev", name, note, [.. new[]
        {
            new PostLinkEmailChange(2021, 8, 1, 2, "Records, revisited", "https://ada.blogspot.com/records", "https://ada.dev/records"),
            new PostLinkEmailChange(2019, 12, 2, 1, "Pattern matching all the way down", "https://ada.blogspot.com/patterns", "https://ada.dev/patterns"),
        }.Take(changes)]);

    private static PostLinksForm Form(params (int SlotId, string? Url)[] links) =>
        new() { Posts = [.. links.Select(link => new PostLinkField { SlotId = link.SlotId, Url = link.Url })] };

    [Fact]
    public void AFreshFormShowsEachPostsLinkAndVersion()
    {
        var form = PostLinksForm.For([Records, Patterns]);

        Assert.Equal([(11, "https://ada.blogspot.com/records", 3u), (12, "https://ada.blogspot.com/patterns", 4u)], form.Posts.Select(p => (p.SlotId, p.Url, p.Version)));
    }

    [Fact]
    public void OnlyLinksThatChangedAreChangesTrimmed()
    {
        var changes = PostLinkService.Changes([Records, Patterns], Form((11, "https://ada.blogspot.com/records"), (12, "  https://ada.dev/patterns  ")));

        var change = Assert.Single(changes);
        Assert.Equal(Patterns, change.Post);
        Assert.Equal("https://ada.dev/patterns", change.NewUrl);
    }

    [Fact]
    public void PostsThatArentTheirsAndEmptyOrRepeatedLinksAreIgnored()
    {
        var changes = PostLinkService.Changes([Records, Patterns], Form((99, "https://evil.example.com"), (11, "  "), (12, "https://ada.dev/patterns"), (12, "https://ada.dev/again")));

        Assert.Equal([(12, "https://ada.dev/patterns")], changes.Select(c => (c.Post.SlotId, c.NewUrl)));
    }

    [Fact]
    public void ALinkThatOnlyDiffersInCaseIsAChange() =>
        Assert.Single(PostLinkService.Changes([Records], Form((11, "https://ada.blogspot.com/Records"))));

    [Fact]
    public void EveryLinkHasToBeAFullWebAddress()
    {
        Assert.Empty(Errors(Form((11, "https://ada.dev/records"), (12, "http://ada.dev/patterns"))));
        Assert.Equal(
            ["“ada.dev/records” isn't a full web address. Links start with https:// or http://."],
            Errors(Form((11, "ada.dev/records"))));
        Assert.Equal(
            ["“ftp://ada.dev/records” isn't a full web address. Links start with https:// or http://."],
            Errors(Form((11, "ftp://ada.dev/records"))));
        Assert.Equal(["Keep each link to 500 characters or fewer."], Errors(Form((11, "https://ada.dev/" + new string('a', 490)))));
    }

    [Fact]
    public void APostCantLoseItsLinkAndEachProblemIsSaidOnce() =>
        Assert.Equal(
            ["Every post needs a link. Put back the one you emptied, or the post's new address."],
            Errors(Form((11, ""), (12, null))));

    [Fact]
    public void TheNoteIsKeptShort() =>
        Assert.Equal(["Keep your note to 1,000 characters or fewer."], Errors(new PostLinksForm { Note = new string('a', 1001) }));

    [Theory]
    [InlineData("https://ada.blogspot.com/patterns", false)]
    [InlineData("https://ada.dev/patterns", false)]
    [InlineData("https://someone-else.example.com", true)]
    public void AReviewSaysWhenTheLinkChangedSinceTheyAsked(string current, bool changedSince) =>
        Assert.Equal(changedSince, new PostLinkChangeView(12, 2019, 12, 2, 2, "Title", "https://ada.blogspot.com/patterns", "https://ada.dev/patterns", current).ChangedSince);

    [Theory]
    [InlineData("Ada Lovelace", "Ada Lovelace (ada-dev)")]
    [InlineData("ADA-DEV", "ada-dev")]
    [InlineData(null, "ada-dev")]
    public void TheRequesterIsNamedWithTheirLogin(string? name, string expected) => Assert.Equal(expected, Request(name).Who);

    [Fact]
    public void TheOrganizersSeeEveryLinkBeforeAndAfter()
    {
        var email = PostLinkEmails.NewRequest(Request(note: null), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("New post links? ada-dev asked to change the links to 2 posts", email.Subject);
        Assert.Contains("“Records, revisited” (December 8, 2021, post 1)", text);
        Assert.Contains("From: https://ada.blogspot.com/records", text);
        Assert.Contains("To: https://ada.dev/records", text);
        Assert.Contains("“Pattern matching all the way down” (December 12, 2019)", text);
        Assert.Equal(
            [("Asked by", "Ada Lovelace (ada-dev), https://github.com/ada-dev"), ("Their note", "None")],
            Assert.Single(email.Body.OfType<DetailsBlock>()).Rows.Select(r => (r.Label, r.Value)));
        Assert.Equal("https://csadvent.christmas/manage/authors/link-changes/9", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void OnePostIsSaidAsOne() =>
        Assert.Equal("New post links? ada-dev asked to change the links to 1 post", PostLinkEmails.NewRequest(Request(changes: 1), Site).Subject);

    [Fact]
    public void AnApprovalListsTheNewLinksAndTheirHistory()
    {
        var email = PostLinkEmails.Approved(Request(), Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("Your C# Advent posts' links are updated", email.Subject);
        Assert.Contains("https://csadvent.christmas/2021/day/8/1", text);
        Assert.Contains("(December 8, 2021, post 1) now goes to https://ada.dev/records", text);
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void ADeclineSaysTheLinksAreAsTheyWereAndInvitesAReply()
    {
        var text = EmailText.Render(PostLinkEmails.Declined(Request(), Site), Site.BaseUrl);

        Assert.Contains("so the links to 2 posts are as they were", text);
        Assert.Contains("“Records, revisited” (December 8, 2021, post 1): https://ada.dev/records", text);
        Assert.Contains("Reply to this email", text);
    }

    [Fact]
    public void PreviewsStayOnOneLine()
    {
        var email = PostLinkEmails.NewRequest(Request(name: "Ada\nLovelace"), Site);

        Assert.DoesNotContain('\n', email.Preview);
        Assert.DoesNotContain('\n', email.Subject);
    }

    [Fact]
    public void TheRequesterIsToldWhateverTheirEmailSettingsAndTheOrganizersReviewUnderAuthors()
    {
        Assert.True(PostLinkEmails.Approved(Request(), Site).Transactional);
        Assert.True(PostLinkEmails.Declined(Request(), Site).Transactional);
        Assert.Equal("my-spots/links", PostLinkLinks.Request);
        Assert.Equal("manage/authors/link-changes/9", PostLinkLinks.Review(9));
    }

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
