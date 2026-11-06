using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261106164701_AllowRepeatedSpotsLeftPosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts");

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts",
            columns: new[] { "season_year", "kind", "platform" },
            unique: true,
            filter: "kind NOT IN ('SignUpsSoon', 'SpotsLeft')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts");

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts",
            columns: new[] { "season_year", "kind", "platform" },
            unique: true,
            filter: "kind <> 'SignUpsSoon'");
    }
}
