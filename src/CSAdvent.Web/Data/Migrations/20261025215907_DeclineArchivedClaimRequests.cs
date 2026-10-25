using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <summary>
/// Archiving a claim request became declining it, which tells the contributor (#143). Requests archived before then keep
/// their decision under the new name; the code no longer knows "Archived".
/// </summary>
public partial class _20261025215907_DeclineArchivedClaimRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("UPDATE claim_requests SET status = 'Declined' WHERE status = 'Archived';");

    /// <summary>The code before knew only Pending, Approved and Archived, so withdrawn requests are archived too.</summary>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("UPDATE claim_requests SET status = 'Archived' WHERE status IN ('Declined', 'Withdrawn');");
}
