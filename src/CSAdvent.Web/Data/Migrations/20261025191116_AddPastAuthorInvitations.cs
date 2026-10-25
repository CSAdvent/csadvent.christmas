using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025191116_AddPastAuthorInvitations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "past_author_invitations",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_past_author_invitations", x => x.id);
                table.ForeignKey(
                    name: "fk_past_author_invitations_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_past_author_invitations_season_year_fingerprint",
            table: "past_author_invitations",
            columns: new[] { "season_year", "fingerprint" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_past_author_invitations_sent_at",
            table: "past_author_invitations",
            column: "sent_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "past_author_invitations");
    }
}
