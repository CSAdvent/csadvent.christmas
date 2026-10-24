using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261024144848_AddAnnouncements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "announcements",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                season_year = table.Column<int>(type: "integer", nullable: true),
                subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                message = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                button_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                button_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_announcements", x => x.id);
                table.CheckConstraint("ck_announcements_button_url", "button_url IS NULL OR button_url ~* '^https?://'");
            });

        migrationBuilder.CreateTable(
            name: "announcement_deliveries",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                announcement_id = table.Column<int>(type: "integer", nullable: false),
                subscriber_id = table.Column<int>(type: "integer", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_announcement_deliveries", x => x.id);
                table.ForeignKey(
                    name: "fk_announcement_deliveries_announcements_announcement_id",
                    column: x => x.announcement_id,
                    principalTable: "announcements",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_announcement_deliveries_subscriber_id",
                    column: x => x.subscriber_id,
                    principalTable: "announcement_subscribers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_announcement_deliveries_announcement_id_subscriber_id",
            table: "announcement_deliveries",
            columns: new[] { "announcement_id", "subscriber_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_announcement_deliveries_sent_at",
            table: "announcement_deliveries",
            column: "sent_at");

        migrationBuilder.CreateIndex(
            name: "ix_announcement_deliveries_subscriber_id",
            table: "announcement_deliveries",
            column: "subscriber_id");

        migrationBuilder.CreateIndex(
            name: "ix_announcements_kind_season_year",
            table: "announcements",
            columns: new[] { "kind", "season_year" },
            unique: true,
            filter: "season_year IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "announcement_deliveries");

        migrationBuilder.DropTable(
            name: "announcements");
    }
}
