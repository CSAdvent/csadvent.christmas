using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261024160227_AddRetrospectives : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_season_social_posts_season_year_platform",
            table: "season_social_posts");

        migrationBuilder.CreateTable(
            name: "retrospectives",
            columns: table => new
            {
                season_year = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                show_charts = table.Column<bool>(type: "boolean", nullable: false),
                stats = table.Column<string>(type: "text", nullable: false),
                stats_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_retrospectives", x => x.season_year);
                table.CheckConstraint("ck_retrospectives_not_test", "season_year <> 9000");
                table.ForeignKey(
                    name: "fk_retrospectives_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts",
            columns: new[] { "season_year", "kind", "platform" },
            unique: true,
            filter: "kind <> 'SignUpsSoon'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "retrospectives");

        migrationBuilder.DropIndex(
            name: "ix_season_social_posts_season_year_kind_platform",
            table: "season_social_posts");

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_platform",
            table: "season_social_posts",
            columns: new[] { "season_year", "platform" },
            unique: true,
            filter: "kind = 'SignUpsOpen'");
    }
}
