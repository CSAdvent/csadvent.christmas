using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025003144_AddPostClaimRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "post_claim_requests",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
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
                table.PrimaryKey("pk_post_claim_requests", x => x.id);
                table.ForeignKey(
                    name: "fk_post_claim_requests_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_post_claim_requests_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_post_claim_requests_contributor_id",
            table: "post_claim_requests",
            column: "contributor_id");

        migrationBuilder.CreateIndex(
            name: "ix_post_claim_requests_slot_id_contributor_id",
            table: "post_claim_requests",
            columns: new[] { "slot_id", "contributor_id" },
            unique: true,
            filter: "status = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "ix_post_claim_requests_status_submitted_at",
            table: "post_claim_requests",
            columns: new[] { "status", "submitted_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "post_claim_requests");
    }
}
