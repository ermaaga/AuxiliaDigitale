using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Directory_ConsentsTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consents",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    granted = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consents", x => x.id);
                    table.CheckConstraint("ck_consents_channel", "channel IN ('Email', 'WhatsApp')");
                    table.CheckConstraint("ck_consents_purpose", "purpose IN ('Marketing', 'Privacy')");
                    table.CheckConstraint("ck_consents_source", "source IN ('Staff', 'Import', 'Api', 'LegacyMigration')");
                    table.ForeignKey(
                        name: "fk_consents_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_consents_user_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "person_tags",
                schema: "directory",
                columns: table => new
                {
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_tags", x => new { x.person_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_person_tags_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_tags_tag_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "directory",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_tags_user_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consents_person_id_purpose_channel_recorded_at",
                schema: "directory",
                table: "consents",
                columns: new[] { "person_id", "purpose", "channel", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_consents_recorded_by_user_id",
                schema: "directory",
                table: "consents",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_tags_assigned_by_user_id",
                schema: "directory",
                table: "person_tags",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_tags_tag_id",
                schema: "directory",
                table: "person_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                schema: "directory",
                table: "tags",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consents",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "person_tags",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "directory");
        }
    }
}
