using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Marketing_Campaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_templates",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suppressions",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", maxLength: 320, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    suppressed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppressions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "marketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    list_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    sent_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_count = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.id);
                    table.CheckConstraint("ck_campaigns_audience", "(segment_id IS NULL) <> (list_id IS NULL)");
                    table.CheckConstraint("ck_campaigns_status", "status IN ('Draft', 'Sending', 'Sent', 'Cancelled', 'Failed')");
                    table.ForeignKey(
                        name: "fk_campaigns_email_template_template_id",
                        column: x => x.template_id,
                        principalSchema: "marketing",
                        principalTable: "email_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaigns_segment_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "marketing",
                        principalTable: "segments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_campaigns_static_list_list_id",
                        column: x => x.list_id,
                        principalSchema: "marketing",
                        principalTable: "static_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_campaigns_users_sent_by_user_id",
                        column: x => x.sent_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_recipients",
                schema: "marketing",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exclusion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    outbound_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_recipients", x => new { x.campaign_id, x.person_id });
                    table.CheckConstraint("ck_campaign_recipients_exclusion", "exclusion IS NULL OR exclusion IN ('NoConsent', 'NoEmail', 'Suppressed', 'Cancelled')");
                    table.CheckConstraint("ck_campaign_recipients_status", "status IN ('Pending', 'Sent', 'Failed', 'Excluded')");
                    table.ForeignKey(
                        name: "fk_campaign_recipients_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "marketing",
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_recipients_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_recipients_campaign_id_status",
                schema: "marketing",
                table: "campaign_recipients",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_recipients_person_id",
                schema: "marketing",
                table: "campaign_recipients",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_list_id",
                schema: "marketing",
                table: "campaigns",
                column: "list_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_segment_id",
                schema: "marketing",
                table: "campaigns",
                column: "segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_sent_by_user_id",
                schema: "marketing",
                table: "campaigns",
                column: "sent_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_template_id",
                schema: "marketing",
                table: "campaigns",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_templates_name",
                schema: "marketing",
                table: "email_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppressions_email",
                schema: "marketing",
                table: "suppressions",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_recipients",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "suppressions",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "email_templates",
                schema: "marketing");
        }
    }
}
