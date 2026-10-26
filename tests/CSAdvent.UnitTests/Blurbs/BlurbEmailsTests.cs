using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class BlurbEmailsTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static readonly BlurbPost Post = new(42, new BackupSpot(2026, 6, 1, 2), "Span<T> all the way down", "https://example.com/span", "10:00 AM Eastern on December 6");

    [Fact]
    public void TheAuthorHearsWhatsMissingTheDeadlineAndTheBackup()
    {
        var email = BlurbEmails.Missing("Ada", Post, "There's no link to csadvent.christmas near the top of the post.", Site);
        var text = Text(email);

        Assert.True(email.Transactional);
        Assert.Equal("Please add the C# Advent blurb to your post for December 6", email.Subject);
        Assert.Contains("There's no link to csadvent.christmas near the top of the post.", text);
        Assert.Contains("This post is part of the 2026 C# Advent calendar (https://csadvent.christmas): two C# posts a day", text);
        Assert.Contains("Please add it by 10:00 AM Eastern on December 6", text);
        Assert.Contains("a backup poster will take your spot", text);
        Assert.Contains("If you'd rather not add it, reply", text);
        Assert.Equal("https://csadvent.christmas/my-spots", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Theory]
    [InlineData(BlurbStatus.NeedsDecision, "C# Advent, December 6: a post's blurb needs your decision")]
    [InlineData(BlurbStatus.CouldNotCheck, "C# Advent, December 6: a post couldn't be checked for the blurb")]
    public void TheOrganizersDecideWhatTheModelsCant(BlurbStatus status, string subject)
    {
        var email = BlurbEmails.NeedsDecision(Post, status, "The models disagree.", Site);

        Assert.Equal(subject, email.Subject);
        Assert.False(email.Transactional);
        Assert.Contains("The models disagree.", Text(email));
        Assert.Equal("https://csadvent.christmas/manage/entries/42", Assert.Single(email.Body.OfType<ButtonBlock>()).Url);
    }

    [Fact]
    public void TheOrganizersHearWhenTheDeadlinePassesWithoutABackup()
    {
        var text = Text(BlurbEmails.DeadlinePassed(Post, Site));

        Assert.Contains("still doesn't open with the C# Advent blurb, and no backup poster has taken the spot", text);
        Assert.Contains("it isn't announced automatically", text);
    }

    [Fact]
    public void TheOrganizersCanKeepTheAuthorBeforeABackupTakesTheSpot()
    {
        var email = BlurbEmails.BackupStandingBy(
            Post,
            new BlurbBackup("Bo Backup", "Collection expressions everywhere", "https://example.com/collections"),
            "There's no link to csadvent.christmas near the top of the post.",
            "10:00 AM Eastern on December 6",
            Site);
        var text = Text(email);

        Assert.Equal("C# Advent, December 6: a backup is standing by for a post without the blurb", email.Subject);
        Assert.False(email.Transactional);
        Assert.Contains("There's no link to csadvent.christmas near the top of the post.", text);
        Assert.Contains("Bo Backup is standing by with Collection expressions everywhere", text);
        Assert.Contains("They take the spot at 10:00 AM Eastern on December 6, unless the author adds the blurb first.", text);
        Assert.Contains("Either one keeps the author", text);
        var button = Assert.Single(email.Body.OfType<ButtonBlock>());
        Assert.Equal(("Keep the author", "https://csadvent.christmas/manage/entries/42#blurb"), (button.Label, button.Url));
    }

    [Fact]
    public void BackupsHearTheyNeedTheBlurbToo()
    {
        var spot = new BackupSpot(2026, 12, 2, 2);
        var text = Text(BackupEmails.Ask("Grace", spot, BackupReason.BlurbMissing, "1:15 AM Eastern on December 12", "https://csadvent.christmas/backups/offers/7", Site, "10:00 AM Eastern on December 12"));

        Assert.Contains("doesn't open with the C# Advent blurb", text);
        Assert.Contains("It has to be published already", text);
        Assert.Contains("can still add the blurb until 10:00 AM Eastern on December 12", text);
        Assert.Contains("because its post didn't open with the C# Advent blurb", Text(BackupEmails.SpotFilled("Grace", spot, BackupReason.BlurbMissing, Site)));
        Assert.Contains("didn't open with the C# Advent blurb by the time it was due to be announced", Text(BackupEmails.Replaced("Ada", spot, Site, BackupReason.BlurbMissing)));
    }

    private static string Text(EmailContent email) => EmailText.Render(email, Site.BaseUrl);
}
