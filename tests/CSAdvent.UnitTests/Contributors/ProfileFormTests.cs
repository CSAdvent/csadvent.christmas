using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Contributors;

public sealed class ProfileFormTests
{
    [Fact]
    public void ANameIsEnough() =>
        Assert.Empty(Errors(new ProfileForm { Name = "Ada Lovelace" }));

    [Fact]
    public void TheNameFitsTheClaimForm() =>
        Assert.Equal(["Keep the name to 50 characters or fewer."], Errors(new ProfileForm { Name = new string('a', 51) }));

    [Fact]
    public void TheLinkMustBeHttp() =>
        Assert.Equal(["Enter a full web address that starts with https:// or http://."], Errors(new ProfileForm { Name = "Ada", Link = "javascript:alert(1)" }));

    [Fact]
    public void SocialAccountsAreCheckedLikeTheClaimForm()
    {
        Assert.Empty(Errors(new ProfileForm { Name = "Ada", Bluesky = "ada.bsky.social", Mastodon = "@ada@mastodon.social", Reddit = "u/ada" }));
        Assert.Equal(["Enter a Mastodon handle, such as @you@mastodon.social."], Errors(new ProfileForm { Name = "Ada", Mastodon = "not a handle" }));
    }

    [Fact]
    public void UnlinkingNeedsConfirmation() =>
        Assert.Equal(["Tick the box to confirm you want to unlink them."], Errors(new UnlinkForm()));

    [Fact]
    public void CombiningNeedsAnAuthor() =>
        Assert.Equal(["Pick the name they also wrote as."], Errors(new CombineForm()));

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
