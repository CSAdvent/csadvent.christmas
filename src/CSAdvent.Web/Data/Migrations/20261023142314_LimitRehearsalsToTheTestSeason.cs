using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261023142314_LimitRehearsalsToTheTestSeason : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons");

        migrationBuilder.AddCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons",
            sql: "(testing_started_at IS NULL) = (rehearsal_starts_on IS NULL) AND (rehearsal_starts_on IS NULL OR year = 9000)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons");

        migrationBuilder.AddCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons",
            sql: "(testing_started_at IS NULL) = (rehearsal_starts_on IS NULL)");
    }
}
