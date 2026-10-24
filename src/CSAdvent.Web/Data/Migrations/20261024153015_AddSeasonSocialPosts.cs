using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261024153015_AddSeasonSocialPosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "season_social_posts",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                marked_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_season_social_posts", x => x.id);
                table.CheckConstraint("ck_season_social_posts_post_url", "post_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_season_social_posts_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_kind",
            table: "season_social_posts",
            columns: new[] { "season_year", "kind" });

        migrationBuilder.CreateIndex(
            name: "ix_season_social_posts_season_year_platform",
            table: "season_social_posts",
            columns: new[] { "season_year", "platform" },
            unique: true,
            filter: "kind = 'SignUpsOpen'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "season_social_posts");
    }
}
