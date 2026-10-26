using CSAdvent.Web.Blurbs;
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

    [Theory]
    [InlineData(2, "two C# posts a day, December 1–25.")]
    [InlineData(1, "a C# post a day, December 1–25.")]
    public void TheReceivedEmailExplainsTheBlurbWithTheExample(int slotsPerDay, string rest)
    {
        var email = ClaimEmails.Received(Claim(slotsPerDay), Site);
        var text = Text(email);

        Assert.Contains("every post opens with the C# Advent blurb", text);
        Assert.Contains(SuggestedBlurb.For(2026, slotsPerDay).Text, text);
        Assert.EndsWith(rest, SuggestedBlurb.For(2026, slotsPerDay).Text);
        Assert.Contains("may go to a backup poster", text);
        var link = Assert.Single(email.Body.OfType<ParagraphBlock>().SelectMany(p => p.Spans).OfType<LinkSpan>(), l => l.Text == "2026 C# Advent calendar");
        Assert.Equal(BlurbRules.SiteUrl, link.Url);
    }

    [Fact]
    public void TheReceivedEmailLeavesTheBlurbOutForSeasonsWithoutTheRule()
    {
        var text = Text(ClaimEmails.Received(Claim() with { Year = BlurbRules.FirstYear - 1 }, Site));

        Assert.DoesNotContain("C# Advent blurb", text);
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
        Assert.Contains("Open your post by saying it's part of the 2026 C# Advent calendar, with a link to csadvent.christmas (https://csadvent.christmas)", text);
        Assert.Contains("one without it when it's announced goes to a backup poster", text);
        Assert.Contains("Keep it to yourself until December 3", text);
        Assert.Contains("sign in and add them on your My spots (https://csadvent.christmas/my-spots) page", text);
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.DoesNotContain("reply", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terms (https://csadvent.christmas/terms)", text);
    }

    [Fact]
    public void TheReceiptSaysTheyHearEitherWayAndCanWithdraw()
    {
        var text = Text(ClaimEmails.Received(Claim(), Site));

        Assert.Contains("you'll get another email once they've decided", text);
        Assert.Contains("Withdraw it from your My spots (https://csadvent.christmas/my-spots) page", text);
    }

    [Fact]
    public void AnApprovalForAnotherSpotSaysSo()
    {
        var moved = ClaimEmails.Approved(Claim() with { Day = 5, Part = 1, RequestedDay = 3, RequestedPart = 2 }, Site);
        var same = ClaimEmails.Approved(Claim() with { RequestedDay = 3, RequestedPart = 2 }, Site);

        Assert.Equal("You're on the C# Advent calendar: December 5", moved.Subject);
        Assert.Contains("December 5, 2026 (post 1) is yours, instead of December 3, 2026 (post 2).", Text(moved));
        Assert.Contains("reply to this email", Text(moved));
        Assert.Contains("Your request is approved: December 3, 2026 (post 2) is yours.", Text(same));
    }

    [Fact]
    public void ADeclinedRequestWhoseSpotWentPointsToTheOpenDays()
    {
        var email = ClaimEmails.Declined(Claim(), ClaimDeclineReason.SpotTaken, Site);

        Assert.Equal("About your C# Advent request for December 3", email.Subject);
        Assert.Contains("That spot has gone to another author", Text(email));
        var button = Assert.Single(email.Body.OfType<ButtonBlock>());
        Assert.Equal(("See the open days", "https://csadvent.christmas"), (button.Label, button.Url));
        Assert.True(email.Transactional);
    }

    [Fact]
    public void ADeclinedRequestOnAFullCalendarPointsToTheBackupList()
    {
        var email = ClaimEmails.Declined(Claim(), ClaimDeclineReason.SeasonFull, Site);

        Assert.Contains("every spot on the calendar is taken now", Text(email));
        Assert.Equal("https://csadvent.christmas/backups/2026", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void ADeclinedRequestWhoseSpotIsStillOpenJustSaysSo()
    {
        var email = ClaimEmails.Declined(Claim(), ClaimDeclineReason.NotApproved, Site);

        Assert.Contains("We couldn't approve your request this time.", Text(email));
        Assert.Empty(email.Body.OfType<ButtonBlock>());
    }

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
