using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261022124749_AddSocialPosts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "social_posts",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                marked_by = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_social_posts", x => x.id);
                table.CheckConstraint("ck_social_posts_post_url", "post_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_social_posts_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_social_posts_slot_id_platform",
            table: "social_posts",
            columns: new[] { "slot_id", "platform" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "social_posts");
    }
}
