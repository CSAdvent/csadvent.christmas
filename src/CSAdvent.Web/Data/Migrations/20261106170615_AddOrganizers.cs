using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261106170615_AddOrganizers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "organizers",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                github_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                github_user_id = table.Column<long>(type: "bigint", nullable: true),
                invited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                invited_by_github_user_id = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_organizers", x => x.id);
                table.CheckConstraint("ck_organizers_login", "github_login = lower(github_login)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_organizers_github_login",
            table: "organizers",
            column: "github_login",
            unique: true,
            filter: "github_user_id IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_organizers_github_user_id",
            table: "organizers",
            column: "github_user_id",
            unique: true);

        // Matt Groves (mgroves) was a default organizer in appsettings.json until organizers could be invited. He stays an
        // organizer, as an invited one, now that only Calvin is a default. The code before this migration ignores the table.
        migrationBuilder.Sql("""
            INSERT INTO organizers (github_login, github_user_id, invited_at)
            VALUES ('mgroves', 103484, now())
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "organizers");
    }
}
