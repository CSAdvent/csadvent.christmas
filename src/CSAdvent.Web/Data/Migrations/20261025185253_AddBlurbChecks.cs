using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025185253_AddBlurbChecks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "blurb_checks",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                quote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                leased_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                backup_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                author_emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                organizers_emailed_about = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                deadline_emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_blurb_checks", x => x.id);
                table.ForeignKey(
                    name: "fk_blurb_checks_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_blurb_checks_slot_id",
            table: "blurb_checks",
            column: "slot_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "blurb_checks");
    }
}
