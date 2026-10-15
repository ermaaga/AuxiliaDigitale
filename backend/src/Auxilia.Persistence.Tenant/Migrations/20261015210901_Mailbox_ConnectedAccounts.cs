using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Mailbox_ConnectedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mailbox");

            migrationBuilder.CreateTable(
                name: "connected_accounts",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    has_mail = table.Column<bool>(type: "boolean", nullable: false),
                    has_calendar = table.Column<bool>(type: "boolean", nullable: false),
                    granted_scopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    imap_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    imap_port = table.Column<int>(type: "integer", nullable: true),
                    imap_security = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    imap_user_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    protected_secret = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_connected_accounts", x => x.id);
                    table.CheckConstraint("ck_connected_accounts_capability", "has_mail OR has_calendar");
                    table.CheckConstraint("ck_connected_accounts_imap", "(provider = 'Imap') = (imap_host IS NOT NULL AND imap_port IS NOT NULL AND imap_security IS NOT NULL AND imap_user_name IS NOT NULL)");
                    table.CheckConstraint("ck_connected_accounts_provider", "provider IN ('Google', 'Microsoft', 'Imap')");
                    table.CheckConstraint("ck_connected_accounts_status", "status IN ('Active', 'NeedsReconnect')");
                    table.ForeignKey(
                        name: "fk_connected_accounts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_requests",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wants_mail = table.Column<bool>(type: "boolean", nullable: false),
                    wants_calendar = table.Column<bool>(type: "boolean", nullable: false),
                    protected_verifier = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_requests", x => x.id);
                    table.CheckConstraint("ck_oauth_requests_provider", "provider IN ('Google', 'Microsoft')");
                    table.ForeignKey(
                        name: "fk_oauth_requests_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_connected_accounts_user_id_provider_address",
                schema: "mailbox",
                table: "connected_accounts",
                columns: new[] { "user_id", "provider", "address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_requests_expires_at",
                schema: "mailbox",
                table: "oauth_requests",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_requests_state_hash",
                schema: "mailbox",
                table: "oauth_requests",
                column: "state_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_requests_user_id",
                schema: "mailbox",
                table: "oauth_requests",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connected_accounts",
                schema: "mailbox");

            migrationBuilder.DropTable(
                name: "oauth_requests",
                schema: "mailbox");
        }
    }
}
