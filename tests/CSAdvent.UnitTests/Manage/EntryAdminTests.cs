using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Data;
using CSAdvent.Web.Manage;

namespace CSAdvent.UnitTests.Manage;

public sealed class EntryAdminTests
{
    [Theory]
    [InlineData(null, null, null, EntryStatus.Open)]
    [InlineData("Ada", null, null, EntryStatus.ClaimedWithoutPost)]
    [InlineData("Ada", "A title", null, EntryStatus.TitleOnly)]
    [InlineData("Ada", null, "https://example.com/post", EntryStatus.UrlOnly)]
    [InlineData("Ada", "A title", "https://example.com/post", EntryStatus.Ready)]
    public void ReportsHowCompleteAnEntryIs(string? author, string? title, string? url, EntryStatus expected) =>
        Assert.Equal(expected, EntryAdminService.StatusOf(author, title, url));

    [Theory]
    [InlineData(null, false, null, null, null, false)]
    [InlineData(null, true, null, null, null, true)]
    [InlineData("Ada", false, null, null, null, true)]
    [InlineData("Ada", false, "A title", null, null, true)]
    [InlineData("Ada", false, null, "https://example.com/post", null, true)]
    [InlineData("Ada", false, "A title", "https://example.com/post", null, false)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.Waiting, false)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.Passed, false)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.Missing, true)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.NeedsDecision, true)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.CouldNotCheck, true)]
    [InlineData("Ada", false, "A title", "https://example.com/post", BlurbStatus.Waived, false)]
    // A backup (or an organizer) filled the spot an author dropped out of.
    [InlineData("Bea", true, "A title", "https://example.com/post", null, false)]
    public void SaysWhichSpotsNeedAnOrganizer(string? author, bool droppedOut, string? title, string? url, BlurbStatus? blurb, bool expected)
    {
        var entry = new EntryListItem(1, 6, 1, author, null, droppedOut, title, url, EntryAdminService.StatusOf(author, title, url));

        Assert.Equal(expected, EntryAdminService.NeedsAttention(entry, blurb, isDayOver: false));
        // Once its day is over there's nothing left to do.
        Assert.False(EntryAdminService.NeedsAttention(entry, blurb, isDayOver: true));
    }

    [Fact]
    public void AnEmptyFormIsValid() => Assert.Empty(Errors(new EntryForm()));

    [Fact]
    public void APostNeedsAnAuthor() =>
        Assert.Equal(["A post needs an author. Enter the author's name, or clear the post."], Errors(new EntryForm { PostTitle = "Orphan" }));

    [Fact]
    public void LinksMustBeHttp() =>
        Assert.Equal(2, Errors(new EntryForm { AuthorName = "Ada", AuthorUrl = "ftp://example.com", PostUrl = "javascript:alert(1)" }).Count);

    [Fact]
    public void EmailsMustBeValid() =>
        Assert.Equal(["Enter a valid email address."], Errors(new EntryForm { AuthorName = "Ada", ContactEmail = "not an email" }));

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
