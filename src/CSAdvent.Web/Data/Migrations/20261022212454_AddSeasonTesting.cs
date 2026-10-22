using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261022212454_AddSeasonTesting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "rehearsal_starts_on",
            table: "seasons",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "testing_started_at",
            table: "seasons",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "season_testers",
            columns: table => new
            {
                season_year = table.Column<int>(type: "integer", nullable: false),
                github_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_season_testers", x => new { x.season_year, x.github_login });
                table.CheckConstraint("ck_season_testers_login", "github_login = lower(github_login)");
                table.ForeignKey(
                    name: "fk_season_testers_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons",
            sql: "(testing_started_at IS NULL) = (rehearsal_starts_on IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "season_testers");

        migrationBuilder.DropCheckConstraint(
            name: "ck_seasons_rehearsal",
            table: "seasons");

        migrationBuilder.DropColumn(
            name: "rehearsal_starts_on",
            table: "seasons");

        migrationBuilder.DropColumn(
            name: "testing_started_at",
            table: "seasons");
    }
}
