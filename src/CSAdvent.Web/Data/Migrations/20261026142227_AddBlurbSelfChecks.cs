using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026142227_AddBlurbSelfChecks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "blurb_self_checks",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                contributor_id = table.Column<int>(type: "integer", nullable: false),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                asked_claude = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_blurb_self_checks", x => x.id);
                table.ForeignKey(
                    name: "fk_blurb_self_checks_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_blurb_self_checks_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_blurb_self_checks_checked_at",
            table: "blurb_self_checks",
            column: "checked_at");

        migrationBuilder.CreateIndex(
            name: "ix_blurb_self_checks_contributor_id_checked_at",
            table: "blurb_self_checks",
            columns: new[] { "contributor_id", "checked_at" });

        migrationBuilder.CreateIndex(
            name: "ix_blurb_self_checks_slot_id",
            table: "blurb_self_checks",
            column: "slot_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "blurb_self_checks");
    }
}
