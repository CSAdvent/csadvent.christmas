using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Social;

namespace CSAdvent.UnitTests.Data;

/// <summary>
/// The backup and social account settings are checked when the app starts, like the rest (#188), so a typo stops a deploy
/// instead of showing up in December. <c>ValidateDataAnnotations</c> runs the same check as <see cref="Validator"/> here.
/// </summary>
public sealed class OptionsValidationTests
{
    [Fact]
    public void TheDefaultsAndAppsettingsAreValid()
    {
        Assert.Empty(Errors(new BackupOptions()));
        Assert.Empty(Errors(new BackupOptions { AskFrom = new(18, 0), AnswerWithin = TimeSpan.FromHours(4), OrganizersNotice = TimeSpan.Zero }));
        Assert.Empty(Errors(new SocialAccountsOptions()));
        Assert.Empty(Errors(new SocialAccountsOptions
        {
            Bluesky = "csadvent.christmas",
            Mastodon = "@csadvent@mastodon.social",
            FacebookUrl = "https://www.facebook.com/csadvent/",
            LinkedInUrl = "https://www.linkedin.com/company/csadvent",
            RedditUser = "CS-Advent",
            Subreddit = "csadvent",
            X = "csadvent",
        }));
    }

    [Fact]
    public void BackupsNeedTimeToAnswer()
    {
        Assert.Equal(
            ["Backups:AnswerWithin and Backups:AnswerWithinOnTheDay must be longer than zero: each backup needs time to answer."],
            Errors(new BackupOptions { AnswerWithin = TimeSpan.Zero }));
        Assert.Single(Errors(new BackupOptions { AnswerWithinOnTheDay = TimeSpan.FromMinutes(-5) }));
        Assert.Equal(["Backups:OrganizersNotice can't be negative."], Errors(new BackupOptions { OrganizersNotice = TimeSpan.FromHours(-1) }));
    }

    [Fact]
    public void SocialAccountsMustBeWhole()
    {
        Assert.Equal(["Social:Mastodon must be the whole account, such as @csadvent@mastodon.social."], Errors(new SocialAccountsOptions { Mastodon = "@csadvent" }));
        Assert.Equal(["Social:FacebookUrl must be the page's https:// address."], Errors(new SocialAccountsOptions { FacebookUrl = "facebook.com/csadvent" }));
        Assert.Equal(["Social:LinkedInUrl must be the page's https:// address."], Errors(new SocialAccountsOptions { LinkedInUrl = "http://www.linkedin.com/company/csadvent" }));
    }

    private static List<string?> Errors(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage)];
    }
}
