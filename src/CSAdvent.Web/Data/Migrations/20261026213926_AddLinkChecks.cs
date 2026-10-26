using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026213926_AddLinkChecks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "link_checks",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                failures = table.Column<int>(type: "integer", nullable: false),
                failing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                dead_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                leased_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                archived_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                archived_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                archive_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_link_checks", x => x.id);
                table.CheckConstraint("ck_link_checks_archived_url", "archived_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_link_checks_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_link_checks_next_check_at",
            table: "link_checks",
            column: "next_check_at");

        migrationBuilder.CreateIndex(
            name: "ix_link_checks_slot_id",
            table: "link_checks",
            column: "slot_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_link_checks_status",
            table: "link_checks",
            column: "status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "link_checks");
    }
}
