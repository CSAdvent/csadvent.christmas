using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CSAdvent.Web.Data.Migrations;

/// <inheritdoc />
public partial class _20261020213351_CreateCoreSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "contributors",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                github_user_id = table.Column<long>(type: "bigint", nullable: false),
                github_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_contributors", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "seasons",
            columns: table => new
            {
                year = table.Column<int>(type: "integer", nullable: false),
                slots_per_day = table.Column<int>(type: "integer", nullable: false),
                claims_open_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                notes = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_seasons", x => x.year);
                table.CheckConstraint("ck_seasons_slots_per_day", "slots_per_day BETWEEN 1 AND 2");
                table.CheckConstraint("ck_seasons_year", "year >= 2017");
            });

        migrationBuilder.CreateTable(
            name: "content_blocks",
            columns: table => new
            {
                key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                body = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_content_blocks", x => x.key);
                table.ForeignKey(
                    name: "fk_content_blocks_contributors_updated_by_id",
                    column: x => x.updated_by_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "other_advents",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                sort_order = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_other_advents", x => x.id);
                table.CheckConstraint("ck_other_advents_url", "url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_other_advents_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "slots",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                day = table.Column<int>(type: "integer", nullable: false),
                part = table.Column<int>(type: "integer", nullable: false),
                author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                author_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                post_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                post_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                contributor_id = table.Column<int>(type: "integer", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_slots", x => x.id);
                table.CheckConstraint("ck_slots_author_url", "author_url ~* '^https?://'");
                table.CheckConstraint("ck_slots_day", "day BETWEEN 1 AND 25");
                table.CheckConstraint("ck_slots_part", "part BETWEEN 1 AND 2");
                table.CheckConstraint("ck_slots_post_requires_author", "author_name IS NOT NULL OR (post_title IS NULL AND post_url IS NULL)");
                table.CheckConstraint("ck_slots_post_url", "post_url ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_slots_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_slots_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "content_block_revisions",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                content_block_key = table.Column<string>(type: "character varying(50)", nullable: false),
                body = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_content_block_revisions", x => x.id);
                table.ForeignKey(
                    name: "fk_content_block_revisions_content_blocks_content_block_key",
                    column: x => x.content_block_key,
                    principalTable: "content_blocks",
                    principalColumn: "key",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_content_block_revisions_contributors_created_by_id",
                    column: x => x.created_by_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "claim_requests",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                slot_id = table.Column<int>(type: "integer", nullable: false),
                contributor_id = table.Column<int>(type: "integer", nullable: false),
                display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                comments = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                timing_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_claim_requests", x => x.id);
                table.CheckConstraint("ck_claim_requests_link", "link ~* '^https?://'");
                table.ForeignKey(
                    name: "fk_claim_requests_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_claim_requests_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sent_reminders",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                run_date = table.Column<DateOnly>(type: "date", nullable: false),
                slot_id = table.Column<int>(type: "integer", nullable: true),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sent_reminders", x => x.id);
                table.ForeignKey(
                    name: "fk_sent_reminders_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "slot_contacts",
            columns: table => new
            {
                slot_id = table.Column<int>(type: "integer", nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_slot_contacts", x => x.slot_id);
                table.ForeignKey(
                    name: "fk_slot_contacts_slots_slot_id",
                    column: x => x.slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "substitutes",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                season_year = table.Column<int>(type: "integer", nullable: false),
                contributor_id = table.Column<int>(type: "integer", nullable: true),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                signed_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                assigned_slot_id = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_substitutes", x => x.id);
                table.ForeignKey(
                    name: "fk_substitutes_contributors_contributor_id",
                    column: x => x.contributor_id,
                    principalTable: "contributors",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_substitutes_seasons_season_year",
                    column: x => x.season_year,
                    principalTable: "seasons",
                    principalColumn: "year",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_substitutes_slots_assigned_slot_id",
                    column: x => x.assigned_slot_id,
                    principalTable: "slots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_claim_requests_contributor_id",
            table: "claim_requests",
            column: "contributor_id");

        migrationBuilder.CreateIndex(
            name: "ix_claim_requests_slot_id",
            table: "claim_requests",
            column: "slot_id");

        migrationBuilder.CreateIndex(
            name: "ix_claim_requests_slot_id_contributor_id",
            table: "claim_requests",
            columns: new[] { "slot_id", "contributor_id" },
            unique: true,
            filter: "status = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "ix_content_block_revisions_content_block_key_created_at",
            table: "content_block_revisions",
            columns: new[] { "content_block_key", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_content_block_revisions_created_by_id",
            table: "content_block_revisions",
            column: "created_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_content_blocks_updated_by_id",
            table: "content_blocks",
            column: "updated_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_contributors_github_user_id",
            table: "contributors",
            column: "github_user_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_other_advents_season_year_sort_order",
            table: "other_advents",
            columns: new[] { "season_year", "sort_order" });

        migrationBuilder.CreateIndex(
            name: "ix_sent_reminders_kind_run_date_slot_id",
            table: "sent_reminders",
            columns: new[] { "kind", "run_date", "slot_id" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "ix_sent_reminders_slot_id",
            table: "sent_reminders",
            column: "slot_id");

        migrationBuilder.CreateIndex(
            name: "ix_slots_contributor_id",
            table: "slots",
            column: "contributor_id");

        migrationBuilder.CreateIndex(
            name: "ix_slots_season_year_day_part",
            table: "slots",
            columns: new[] { "season_year", "day", "part" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_substitutes_assigned_slot_id",
            table: "substitutes",
            column: "assigned_slot_id");

        migrationBuilder.CreateIndex(
            name: "ix_substitutes_contributor_id",
            table: "substitutes",
            column: "contributor_id");

        migrationBuilder.CreateIndex(
            name: "ix_substitutes_season_year_contributor_id",
            table: "substitutes",
            columns: new[] { "season_year", "contributor_id" },
            unique: true,
            filter: "contributor_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_substitutes_season_year_signed_up_at",
            table: "substitutes",
            columns: new[] { "season_year", "signed_up_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "claim_requests");

        migrationBuilder.DropTable(
            name: "content_block_revisions");

        migrationBuilder.DropTable(
            name: "other_advents");

        migrationBuilder.DropTable(
            name: "sent_reminders");

        migrationBuilder.DropTable(
            name: "slot_contacts");

        migrationBuilder.DropTable(
            name: "substitutes");

        migrationBuilder.DropTable(
            name: "content_blocks");

        migrationBuilder.DropTable(
            name: "slots");

        migrationBuilder.DropTable(
            name: "contributors");

        migrationBuilder.DropTable(
            name: "seasons");
    }
}
