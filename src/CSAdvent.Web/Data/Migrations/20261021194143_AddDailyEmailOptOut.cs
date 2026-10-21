using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261021194143_AddDailyEmailOptOut : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "daily_emails_opted_out_at",
            table: "slot_contacts",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "daily_emails_opted_out_at",
            table: "slot_contacts");
    }
}
