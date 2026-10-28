using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261028180504_AddPostSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "post_snapshots",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                moment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                leased_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                snapshot_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_post_snapshots", x => x.id);
                table.CheckConstraint("ck_post_snapshots_snapshot_url", "snapshot_url ~ '^https://web\\.archive\\.org/web/'");
                table.ForeignKey(
                    name: "fk_post_snapshots_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_post_snapshots_slot_id_moment_post_url",
            table: "post_snapshots",
            columns: new[] { "slot_id", "moment", "post_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_post_snapshots_status_next_attempt_at",
            table: "post_snapshots",
            columns: new[] { "status", "next_attempt_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "post_snapshots");
    }
}
