using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261021212140_AddEntryHits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "entry_hits",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                via = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_entry_hits", x => x.id);
                table.ForeignKey(
                    name: "fk_entry_hits_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_entry_hits_at",
            table: "entry_hits",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "ix_entry_hits_slot_id_kind",
            table: "entry_hits",
            columns: new[] { "slot_id", "kind" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "entry_hits");
    }
}
