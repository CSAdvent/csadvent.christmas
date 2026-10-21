using System.ComponentModel.DataAnnotations;
using CSAdvent.UnitTests.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Claims;

public sealed class ClaimFormTests
{
    private static ClaimForm Valid() => new() { DisplayName = "Ada", AgreeToTiming = true, AgreeToTerms = true };

    private static List<string> Errors(ClaimForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage!)];
    }

    [Fact]
    public void AcceptsAValidForm() => Assert.Empty(Errors(Valid()));

    [Theory]
    [InlineData("")]
    [InlineData("This display name is far longer than the fifty characters allowed")]
    public void RequiresAShortName(string name)
    {
        var form = Valid();
        form.DisplayName = name;

        Assert.Single(Errors(form));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/")]
    [InlineData("example.com")]
    public void RejectsLinksThatArentHttp(string link)
    {
        var form = Valid();
        form.Link = link;

        Assert.Equal(["Enter a full web address that starts with https:// or http://."], Errors(form));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/me")]
    public void AcceptsAnEmptyOrHttpLink(string? link)
    {
        var form = Valid();
        form.Link = link;

        Assert.Empty(Errors(form));
    }

    [Fact]
    public void RequiresBothAgreements()
    {
        var form = Valid();
        form.AgreeToTiming = false;
        form.AgreeToTerms = false;

        Assert.Equal(2, Errors(form).Count);
    }
}

public sealed class ClaimAvailabilityTests
{
    private static readonly DateTimeOffset ClaimsOpened = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4));

    private static ClaimAvailability Check(DateTimeOffset? claimsOpenAt, int day, int part, DateTimeOffset now)
    {
        var season = CalendarFixtures.BuildSeason(claimsOpenAt);
        Slot slot = season.Slots.Single(s => s.Day == day && s.Part == part);
        return ClaimService.CheckAvailability(season, slot, CalendarFixtures.Schedule(now));
    }

    [Fact]
    public void OpenSpotsCanBeClaimedOnceClaimsOpen() =>
        Assert.Equal(ClaimAvailability.Open, Check(ClaimsOpened, day: 11, part: 2, CalendarFixtures.December10));

    [Fact]
    public void NothingCanBeClaimedBeforeClaimsOpen() =>
        Assert.Equal(ClaimAvailability.ClaimsNotOpen, Check(ClaimsOpened, day: 11, part: 2, ClaimsOpened.AddSeconds(-1)));

    [Fact]
    public void NothingCanBeClaimedWhenClaimsArentScheduled() =>
        Assert.Equal(ClaimAvailability.ClaimsNotOpen, Check(claimsOpenAt: null, day: 11, part: 2, CalendarFixtures.December10));

    [Fact]
    public void ClaimedSpotsCantBeClaimedAgain() =>
        Assert.Equal(ClaimAvailability.AlreadyClaimed, Check(ClaimsOpened, day: 11, part: 1, CalendarFixtures.December10));

    [Fact]
    public void RevealedDaysCantBeClaimed() =>
        Assert.Equal(ClaimAvailability.AlreadyRevealed, Check(ClaimsOpened, day: 9, part: 2, CalendarFixtures.December10));
}
