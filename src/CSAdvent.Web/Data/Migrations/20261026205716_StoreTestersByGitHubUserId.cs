using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026205716_StoreTestersByGitHubUserId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "github_user_id",
            table: "season_testers",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_season_testers_season_year_github_user_id",
            table: "season_testers",
            columns: new[] { "season_year", "github_user_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_season_testers_season_year_github_user_id",
            table: "season_testers");

        migrationBuilder.DropColumn(
            name: "github_user_id",
            table: "season_testers");
    }
}
