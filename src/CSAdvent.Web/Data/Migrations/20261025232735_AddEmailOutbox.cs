using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261025232735_AddEmailOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "email_outbox",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                to_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                reply_to = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                subject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                html = table.Column<string>(type: "text", nullable: false),
                text = table.Column<string>(type: "text", nullable: false),
                unsubscribe_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                transactional = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                send_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_email_outbox", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_email_outbox_finished_at",
            table: "email_outbox",
            column: "finished_at");

        migrationBuilder.CreateIndex(
            name: "ix_email_outbox_priority_id",
            table: "email_outbox",
            columns: new[] { "priority", "id" },
            filter: "status = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "ix_email_outbox_reference",
            table: "email_outbox",
            column: "reference");

        migrationBuilder.CreateIndex(
            name: "ix_email_outbox_sent_at",
            table: "email_outbox",
            column: "sent_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "email_outbox");
    }
}
