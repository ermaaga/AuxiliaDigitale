using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Mailbox_SyncRunsAndCalendarEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "calendar_synced_at",
                schema: "mailbox",
                table: "connected_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mail_synced_at",
                schema: "mailbox",
                table: "connected_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "calendar_events",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    tentative = table.Column<bool>(type: "boolean", nullable: false),
                    link = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_events", x => x.id);
                    table.CheckConstraint("ck_calendar_events_range", "ends_at >= starts_at");
                    table.ForeignKey(
                        name: "fk_calendar_events_connected_account_account_id",
                        column: x => x.account_id,
                        principalSchema: "mailbox",
                        principalTable: "connected_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_cursors",
                schema: "mailbox",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folder_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    page_token = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    sync_token = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    full_sync_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_cursors", x => new { x.account_id, x.scope, x.folder_key });
                    table.CheckConstraint("ck_sync_cursors_scope", "scope IN ('Mail', 'Calendar')");
                    table.ForeignKey(
                        name: "fk_sync_cursors_connected_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "mailbox",
                        principalTable: "connected_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_runs",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    updated_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    parts = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_runs", x => x.id);
                    table.CheckConstraint("ck_sync_runs_scope", "scope IN ('Mail', 'Calendar')");
                    table.CheckConstraint("ck_sync_runs_status", "status IN ('Queued', 'Running', 'Succeeded', 'Failed')");
                    table.CheckConstraint("ck_sync_runs_trigger", "trigger IN ('PageOpen', 'Manual', 'Scheduled')");
                    table.ForeignKey(
                        name: "fk_sync_runs_connected_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "mailbox",
                        principalTable: "connected_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_account_id_provider_event_id",
                schema: "mailbox",
                table: "calendar_events",
                columns: new[] { "account_id", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_account_id_starts_at",
                schema: "mailbox",
                table: "calendar_events",
                columns: new[] { "account_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_account_id_scope_queued_at",
                schema: "mailbox",
                table: "sync_runs",
                columns: new[] { "account_id", "scope", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ux_sync_runs_active",
                schema: "mailbox",
                table: "sync_runs",
                columns: new[] { "account_id", "scope" },
                unique: true,
                filter: "status IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_events",
                schema: "mailbox");

            migrationBuilder.DropTable(
                name: "sync_cursors",
                schema: "mailbox");

            migrationBuilder.DropTable(
                name: "sync_runs",
                schema: "mailbox");

            migrationBuilder.DropColumn(
                name: "calendar_synced_at",
                schema: "mailbox",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "mail_synced_at",
                schema: "mailbox",
                table: "connected_accounts");
        }
    }
}
