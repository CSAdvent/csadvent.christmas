using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025200241_AddContributorXUsername : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "x_username",
            table: "contributors",
            type: "character varying(15)",
            maxLength: 15,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "x_username",
            table: "contributors");
    }
}
