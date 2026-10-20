using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Data;

public sealed class SnakeCaseNamingConventionTests
{
    [Theory]
    [InlineData("Slots", "slots")]
    [InlineData("PostUrl", "post_url")]
    [InlineData("GithubUserId", "github_user_id")]
    [InlineData("PK_slots", "pk_slots")]
    [InlineData("FK_slots_seasons_season_year", "fk_slots_seasons_season_year")]
    [InlineData("HTMLPage", "html_page")]
    [InlineData("Day2Part", "day2_part")]
    [InlineData("xmin", "xmin")]
    [InlineData("already_snake_case", "already_snake_case")]
    public void ConvertsNamesToSnakeCase(string name, string expected) =>
        Assert.Equal(expected, SnakeCaseNamingConvention.ToSnakeCase(name));
}
