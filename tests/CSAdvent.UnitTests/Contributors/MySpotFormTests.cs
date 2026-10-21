using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Contributors;

namespace CSAdvent.UnitTests.Contributors;

public sealed class MySpotFormTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("A title", "https://example.com/post", true)]
    [InlineData("A title", null, false)]
    [InlineData(null, "https://example.com/post", false)]
    public void ThePostsTitleAndUrlGoTogether(string? title, string? url, bool valid) =>
        Assert.Equal(valid, Errors(new MySpotForm { AuthorName = "Ada", PostTitle = title, PostUrl = url }).Count == 0);

    [Fact]
    public void LinksMustBeHttp() =>
        Assert.Equal(2, Errors(new MySpotForm { AuthorName = "Ada", AuthorUrl = "ftp://example.com", PostTitle = "Title", PostUrl = "javascript:alert(1)" }).Count);

    [Fact]
    public void DroppingOutNeedsConfirmation() =>
        Assert.Equal(["Tick the box to confirm you're giving up this spot."], Errors(new DropOutForm()));

    private static List<string> Errors(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }
}
