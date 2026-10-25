using CSAdvent.Web.Announcements;
using CSAdvent.Web.Content;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Content;

public sealed class PastAuthorInvitationTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas/" };

    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

    [Fact]
    public void EachAddressIsInvitedOnceWithTheirHistory()
    {
        PastSpot[] spots =
        [
            new(1, 2019, true, "Ada L.", null, null, "Ada@Example.com"),
            new(2, 2023, true, "Ada Lovelace", null, null, "ada@example.com"),
            // A spot they had without a post still makes them a past author, but isn't a post.
            new(3, 2024, false, "Ada Lovelace", null, null, "ada@example.com"),
            // Linked to an account: its verified email, and the profile name over the name on the post.
            new(4, 2021, true, "G. Hopper", "grace@example.com", "Grace Hopper", "grace-old@example.com"),
            new(5, 2022, true, "Grace", "grace@example.com", null, null),
        ];

        var authors = PastAuthorInvitationService.Plan(spots, Nothing, Nothing);

        Assert.Equal(
            [
                new PastAuthor("ada@example.com", "Ada Lovelace", 2, 2019),
                new PastAuthor("grace-old@example.com", "Grace Hopper", 1, 2021),
                new PastAuthor("grace@example.com", "Grace Hopper", 2, 2021),
            ],
            authors);
    }

    [Fact]
    public void SubscribersNewSeasonAuthorsTheInvitedAndTheUnsubscribedAreLeftOut()
    {
        PastSpot[] spots =
        [
            new(1, 2023, true, "Ada", null, null, "ada@example.com"),
            new(2, 2023, true, "Grace", null, null, "grace@example.com"),
            new(3, 2023, true, "Linus", null, null, "linus@example.com"),
        ];

        var authors = PastAuthorInvitationService.Plan(
            spots,
            new HashSet<string>(["ada@example.com"], StringComparer.Ordinal),
            new HashSet<string>([EmailOptOutLinks.Fingerprint("Grace@Example.com")], StringComparer.Ordinal));

        Assert.Equal(["linus@example.com"], authors.Select(a => a.Address));
    }

    [Theory]
    [InlineData(3, "since you've written 3 posts for C# Advent since 2019, we'd love to have you back.")]
    [InlineData(1, "since you wrote a post for C# Advent 2019, we'd love to have you back.")]
    [InlineData(0, "since you've taken part in C# Advent before, we'd love to have you back.")]
    public void TheInvitationIsPersonalAndOptional(int posts, string history)
    {
        var email = PastAuthorEmails.Invitation(new PastAuthor("ada@example.com", "Ada Lovelace", posts, 2019), 2026, Site);
        var text = EmailText.Render(email, Site.BaseUrl);

        Assert.Equal("C# Advent 2026 sign-ups are open: you wrote for us before", email.Subject);
        Assert.Equal("Come back for C# Advent 2026, Ada Lovelace!", email.Heading);
        Assert.Contains(history, text);
        Assert.Equal("https://csadvent.christmas/?via=email", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
        Assert.Contains("https://csadvent.christmas/backups/2026", text);
        Assert.False(email.Transactional);
        Assert.Null(email.UnsubscribeUrl);
    }
}
