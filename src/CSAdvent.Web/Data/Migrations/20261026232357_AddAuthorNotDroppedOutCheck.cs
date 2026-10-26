using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026232357_AddAuthorNotDroppedOutCheck : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // TightenDataModel cleared these, but the code before #188, which gave a dropped spot an author without clearing its
        // drop-out time, kept serving while that migration ran. So the rows the rule would refuse are fixed first (#245).
        migrationBuilder.Sql("UPDATE slots SET dropped_out_at = NULL WHERE author_name IS NOT NULL AND dropped_out_at IS NOT NULL;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_slots_author_not_dropped_out",
            table: "slots",
            sql: "author_name IS NULL OR dropped_out_at IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_slots_author_not_dropped_out",
            table: "slots");
    }
}
