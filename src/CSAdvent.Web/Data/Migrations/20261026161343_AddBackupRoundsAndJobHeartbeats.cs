using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026161343_AddBackupRoundsAndJobHeartbeats : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_backup_offers_slot_id_substitute_id",
            table: "backup_offers");

        migrationBuilder.CreateTable(
            name: "backup_rounds",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                holder = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                offers_after = table.Column<int>(type: "integer", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                nobody_left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_rounds", x => x.id);
                table.ForeignKey(
                    name: "fk_backup_rounds_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "job_heartbeats",
            columns: table => new
            {
                step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_job_heartbeats", x => x.step);
            });

        migrationBuilder.CreateIndex(
            name: "ix_backup_offers_one_ask",
            table: "backup_offers",
            columns: new[] { "slot_id", "substitute_id" },
            unique: true,
            filter: "status = 'Asked'");

        migrationBuilder.CreateIndex(
            name: "ix_backup_rounds_slot_id_number",
            table: "backup_rounds",
            columns: new[] { "slot_id", "number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backup_rounds");

        migrationBuilder.DropTable(
            name: "job_heartbeats");

        migrationBuilder.DropIndex(
            name: "ix_backup_offers_one_ask",
            table: "backup_offers");

        migrationBuilder.CreateIndex(
            name: "ix_backup_offers_slot_id_substitute_id",
            table: "backup_offers",
            columns: new[] { "slot_id", "substitute_id" },
            unique: true);
    }
}
