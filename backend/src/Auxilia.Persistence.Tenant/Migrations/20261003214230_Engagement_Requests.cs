using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Engagement_Requests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engagement");

            migrationBuilder.CreateTable(
                name: "requests",
                schema: "engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requests", x => x.id);
                    table.CheckConstraint("ck_requests_status", "status IN ('Pending', 'Responded', 'Closed')");
                    table.CheckConstraint("ck_requests_type", "type IN ('Information', 'General', 'Support', 'Appointment')");
                    table.ForeignKey(
                        name: "fk_requests_user_closed_by_user_id",
                        column: x => x.closed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_requests_user_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_requests_user_sender_user_id",
                        column: x => x.sender_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "request_messages",
                schema: "engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_request_messages_requests_request_id",
                        column: x => x.request_id,
                        principalSchema: "engagement",
                        principalTable: "requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_request_messages_user_author_user_id",
                        column: x => x.author_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_request_messages_author_user_id",
                schema: "engagement",
                table: "request_messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_messages_request_id_sequence",
                schema: "engagement",
                table: "request_messages",
                columns: new[] { "request_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requests_closed_by_user_id",
                schema: "engagement",
                table: "requests",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requests_recipient_user_id_sent_at",
                schema: "engagement",
                table: "requests",
                columns: new[] { "recipient_user_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_sender_user_id_sent_at",
                schema: "engagement",
                table: "requests",
                columns: new[] { "sender_user_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_status_sent_at",
                schema: "engagement",
                table: "requests",
                columns: new[] { "status", "sent_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_messages",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "requests",
                schema: "engagement");
        }
    }
}
