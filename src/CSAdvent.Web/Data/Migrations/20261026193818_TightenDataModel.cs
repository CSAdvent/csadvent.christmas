using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261026193818_TightenDataModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The rows the rules below would refuse are fixed first, so the migration can't fail on what's already there (#188).
        // Approving a request into a dropped spot, or an organizer filling one, kept its drop-out time before #188. The
        // constraint that a spot with an author isn't dropped out waits for a later release (Slot.cs says why), but the data is
        // ready for it.
        migrationBuilder.Sql("UPDATE slots SET dropped_out_at = NULL WHERE author_name IS NOT NULL AND dropped_out_at IS NOT NULL;");

        // No code links an open spot to anyone, but a spot without an author can't be anyone's.
        migrationBuilder.Sql("UPDATE slots SET contributor_id = NULL WHERE author_name IS NULL AND contributor_id IS NOT NULL;");

        // Only the test season is ever deleted, and it never has a "sign-ups are open" announcement, so there should be none
        // about a missing season. One that's there keeps its history, without the season.
        migrationBuilder.Sql(
            "UPDATE announcements SET season_year = NULL " +
            "WHERE season_year IS NOT NULL AND NOT EXISTS (SELECT 1 FROM seasons WHERE seasons.year = announcements.season_year);");

        // Reminder kinds no version ever recorded, now gone from ReminderKind: EF Core couldn't read a row with one back.
        migrationBuilder.Sql("DELETE FROM sent_reminders WHERE kind IN ('ContributorOnTrack', 'AdminMissingContact', 'AdminDailySummary');");

        migrationBuilder.AddCheckConstraint(
            name: "ck_slots_contributor_needs_author",
            table: "slots",
            sql: "contributor_id IS NULL OR author_name IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_announcements_season_year",
            table: "announcements",
            column: "season_year");

        migrationBuilder.AddForeignKey(
            name: "fk_announcements_seasons_season_year",
            table: "announcements",
            column: "season_year",
            principalTable: "seasons",
            principalColumn: "year",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_announcements_seasons_season_year",
            table: "announcements");

        migrationBuilder.DropCheckConstraint(
            name: "ck_slots_contributor_needs_author",
            table: "slots");

        migrationBuilder.DropIndex(
            name: "ix_announcements_season_year",
            table: "announcements");
    }
}
