using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261021205708_AddBackupOffers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "link",
            table: "substitutes",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "backup_offers",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                substitute_id = table.Column<int>(type: "integer", nullable: false),
                asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                author_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                post_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_offers", x => x.id);
                table.CheckConstraint("ck_backup_offers_author_url", "author_url ~* '^https?://'");
                table.CheckConstraint("ck_backup_offers_post_url", "post_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_backup_offers_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_backup_offers_substitutes_substitute_id",
                    column: x => x.substitute_id,
                    principalTable: "substitutes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_substitutes_link",
            table: "substitutes",
            sql: "link ~* '^https?://'");

        migrationBuilder.CreateIndex(
            name: "ix_backup_offers_one_taker",
            table: "backup_offers",
            column: "slot_id",
            unique: true,
            filter: "status = 'Accepted'");

        migrationBuilder.CreateIndex(
            name: "ix_backup_offers_slot_id_substitute_id",
            table: "backup_offers",
            columns: new[] { "slot_id", "substitute_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_backup_offers_substitute_id",
            table: "backup_offers",
            column: "substitute_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backup_offers");

        migrationBuilder.DropCheckConstraint(
            name: "ck_substitutes_link",
            table: "substitutes");

        migrationBuilder.DropColumn(
            name: "link",
            table: "substitutes");
    }
}
