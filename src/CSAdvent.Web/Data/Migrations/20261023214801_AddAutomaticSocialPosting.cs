using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261023214801_AddAutomaticSocialPosting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "attempts",
            table: "social_posts",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "social_platform_settings",
            columns: table => new
            {
                platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                check_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_social_platform_settings", x => x.platform);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "social_platform_settings");

        migrationBuilder.DropColumn(
            name: "attempts",
            table: "social_posts");
    }
}
