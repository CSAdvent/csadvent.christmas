using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025170733_AddPostLinkRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "post_link_requests",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                contributor_id = table.Column<int>(type: "integer", nullable: false),
                note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                decided_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_post_link_requests", x => x.id);
                table.ForeignKey(
                    name: "fk_post_link_requests_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "post_link_changes",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                request_id = table.Column<int>(type: "integer", nullable: false),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                old_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                new_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_post_link_changes", x => x.id);
                table.CheckConstraint("ck_post_link_changes_new_url", "new_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_post_link_changes_post_link_requests_request_id",
                    column: x => x.request_id,
                    principalTable: "post_link_requests",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_post_link_changes_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_post_link_changes_request_id_slot_id",
            table: "post_link_changes",
            columns: new[] { "request_id", "slot_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_post_link_changes_slot_id",
            table: "post_link_changes",
            column: "slot_id");

        migrationBuilder.CreateIndex(
            name: "ix_post_link_requests_contributor_id",
            table: "post_link_requests",
            column: "contributor_id",
            unique: true,
            filter: "status = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "ix_post_link_requests_contributor_id_submitted_at",
            table: "post_link_requests",
            columns: new[] { "contributor_id", "submitted_at" });

        migrationBuilder.CreateIndex(
            name: "ix_post_link_requests_status_submitted_at",
            table: "post_link_requests",
            columns: new[] { "status", "submitted_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "post_link_changes");

        migrationBuilder.DropTable(
            name: "post_link_requests");
    }
}
