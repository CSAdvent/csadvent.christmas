using System.Globalization;
using CSAdvent.Web.Content;
using CSAdvent.Web.DeadLinks;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class DeadLinkEmailTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly DeadPost Archived = new(
        41, 2019, 12, 2, 2, "Pattern matching", "Ada", "https://ada.example.com/pattern-matching",
        "The site's address, ada.example.com, doesn't exist any more.", "https://web.archive.org/web/2019/https://ada.example.com/pattern-matching", true);

    private static readonly DeadPost NoCopy = new(
        17, 2017, 5, 1, 1, "Async all the way", "Grace", "https://grace.example.com/async", "The page answered 404 Not Found.", null, true);

    private static readonly DeadPost NotLookedUp = NoCopy with { SlotId = 18, CopyLookedUp = false };

    [Theory]
    // Before 8 AM Eastern it's yesterday's 8 AM, and from then on today's.
    [InlineData("2026-10-27T11:59:00Z", "2026-10-26T12:00:00Z")]
    [InlineData("2026-10-27T12:00:00Z", "2026-10-27T12:00:00Z")]
    [InlineData("2026-10-27T23:00:00Z", "2026-10-27T12:00:00Z")]
    // Daylight saving time ends on November 1, 2026: yesterday's 8 AM was EDT, and today's is EST.
    [InlineData("2026-11-01T12:30:00Z", "2026-10-31T12:00:00Z")]
    [InlineData("2026-11-01T13:00:00Z", "2026-11-01T13:00:00Z")]
    public void PostsFoundDeadBeforeTheLatestEightAmEasternAreDue(string now, string cutoff) =>
        Assert.Equal(Parse(cutoff), DeadLinkEmail.Cutoff(Parse(now), new TimeOnly(8, 0), Eastern));

    [Fact]
    public void APostSaysWhatTheCheckFoundAndWhereReadersGo()
    {
        var email = DeadLinkEmail.Email([Archived], 3, Site);
        var text = Text(email);

        Assert.Equal("An older C# Advent post is gone", email.Subject);
        Assert.Equal("Readers now get its saved copy.", email.Preview);
        Assert.Contains("couldn't load this post 3 times in a row, days apart, so it's marked dead.", text);
        Assert.Contains(
            "- December 12, 2019 · Post 2: Pattern matching by Ada. https://ada.example.com/pattern-matching The site's address, " +
            "ada.example.com, doesn't exist any more. Readers get its saved copy (https://web.archive.org/web/2019/https://ada.example.com/pattern-matching). " +
            "Open (https://csadvent.christmas/manage/authors/dead-links#post-41)",
            text);
        Assert.Equal("https://csadvent.christmas/manage/authors/dead-links", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void SeveralPostsAreOneEmailSayingWhichHaveASavedCopy()
    {
        var email = DeadLinkEmail.Email([Archived, NoCopy, NotLookedUp], 3, Site);
        var text = Text(email);

        Assert.Equal("3 older C# Advent posts are gone", email.Subject);
        Assert.Equal("Readers now get a saved copy of 1 of them.", email.Preview);
        Assert.Equal(3, Assert.Single(email.Body.OfType<ListBlock>()).Items.Count);
        Assert.Contains(
            "- December 5, 2017: Async all the way by Grace. https://grace.example.com/async The page answered 404 Not Found. " +
            "The Wayback Machine has no copy of it, so readers still go to its address.",
            text);
        Assert.Contains("It hasn't been looked up at the Wayback Machine yet, so readers still go to its address.", text);

        Assert.Equal("There's no saved copy of any of them yet.", DeadLinkEmail.Email([NoCopy, NotLookedUp], 3, Site).Preview);
        Assert.Equal("Readers now get a saved copy of each of them.", DeadLinkEmail.Email([Archived, Archived with { SlotId = 42 }], 3, Site).Preview);
        Assert.Equal("There's no saved copy of it yet, so readers still go to its address.", DeadLinkEmail.Email([NoCopy], 3, Site).Preview);
    }

    [Fact]
    public void AnAuthorsWordsStayOnOneLine()
    {
        var text = Text(DeadLinkEmail.Email([NoCopy with { Title = "Async\nall the way", AuthorName = "Grace\r\nHopper" }], 3, Site));

        Assert.Contains("- December 5, 2017: Asyncall the way by GraceHopper. https://grace.example.com/async", text);
    }

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
