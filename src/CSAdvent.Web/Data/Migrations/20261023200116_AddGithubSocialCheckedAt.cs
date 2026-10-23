using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261023200116_AddGithubSocialCheckedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "github_social_checked_at",
            table: "contributors",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "github_social_checked_at",
            table: "contributors");
    }
}
