using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261021131439_AddContributorSocialAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "bluesky_handle",
            table: "contributors",
            type: "character varying(253)",
            maxLength: 253,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "facebook_url",
            table: "contributors",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "linkedin_url",
            table: "contributors",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "mastodon_handle",
            table: "contributors",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "reddit_username",
            table: "contributors",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "bluesky_handle",
            table: "contributors");

        migrationBuilder.DropColumn(
            name: "facebook_url",
            table: "contributors");

        migrationBuilder.DropColumn(
            name: "linkedin_url",
            table: "contributors");

        migrationBuilder.DropColumn(
            name: "mastodon_handle",
            table: "contributors");

        migrationBuilder.DropColumn(
            name: "reddit_username",
            table: "contributors");
    }
}
