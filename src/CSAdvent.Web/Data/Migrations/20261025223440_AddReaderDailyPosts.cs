using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025223440_AddReaderDailyPosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "daily_posts_since",
            table: "announcement_subscribers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "daily_post_deliveries",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                day = table.Column<int>(type: "integer", nullable: false),
                subscriber_id = table.Column<int>(type: "integer", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_daily_post_deliveries", x => x.id);
                table.CheckConstraint("ck_daily_post_deliveries_day", "day BETWEEN 1 AND 25");
                table.ForeignKey(
                    name: "fk_daily_post_deliveries_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_daily_post_deliveries_subscriber_id",
                    column: x => x.subscriber_id,
                    principalTable: "announcement_subscribers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_daily_post_deliveries_season_year_day_subscriber_id",
            table: "daily_post_deliveries",
            columns: new[] { "season_year", "day", "subscriber_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_daily_post_deliveries_sent_at",
            table: "daily_post_deliveries",
            column: "sent_at");

        migrationBuilder.CreateIndex(
            name: "ix_daily_post_deliveries_subscriber_id",
            table: "daily_post_deliveries",
            column: "subscriber_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "daily_post_deliveries");

        migrationBuilder.DropColumn(
            name: "daily_posts_since",
            table: "announcement_subscribers");
    }
}
