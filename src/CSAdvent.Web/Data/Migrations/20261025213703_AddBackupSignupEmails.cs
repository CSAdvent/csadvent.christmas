using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025213703_AddBackupSignupEmails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "backup_signup_emails",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                contributor_id = table.Column<int>(type: "integer", nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_backup_signup_emails", x => x.id);
                table.ForeignKey(
                    name: "fk_backup_signup_emails_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_backup_signup_emails_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_backup_signup_emails_contributor_id",
            table: "backup_signup_emails",
            column: "contributor_id");

        migrationBuilder.CreateIndex(
            name: "ix_backup_signup_emails_season_year_contributor_id",
            table: "backup_signup_emails",
            columns: new[] { "season_year", "contributor_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "backup_signup_emails");
    }
}
