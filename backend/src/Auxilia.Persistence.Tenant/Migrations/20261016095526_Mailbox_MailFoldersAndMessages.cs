using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Mailbox_MailFoldersAndMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "arrived_count",
                schema: "mailbox",
                table: "sync_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "mail_folders",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_folder_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_folders", x => x.id);
                    table.CheckConstraint("ck_mail_folders_kind", "kind IN ('Inbox', 'Sent')");
                    table.ForeignKey(
                        name: "fk_mail_folders_connected_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "mailbox",
                        principalTable: "connected_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_messages",
                schema: "mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    internet_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    thread_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    to_recipients = table.Column<string[]>(type: "text[]", nullable: false),
                    cc_recipients = table.Column<string[]>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    attachment_names = table.Column<string[]>(type: "text[]", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_messages", x => x.id);
                    table.CheckConstraint("ck_mail_messages_size", "size >= 0");
                    table.ForeignKey(
                        name: "fk_mail_messages_connected_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "mailbox",
                        principalTable: "connected_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mail_messages_mail_folders_folder_id",
                        column: x => x.folder_id,
                        principalSchema: "mailbox",
                        principalTable: "mail_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mail_folders_account_id_kind",
                schema: "mailbox",
                table: "mail_folders",
                columns: new[] { "account_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_account_id_received_at",
                schema: "mailbox",
                table: "mail_messages",
                columns: new[] { "account_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_folder_id_provider_message_id",
                schema: "mailbox",
                table: "mail_messages",
                columns: new[] { "folder_id", "provider_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_folder_id_received_at",
                schema: "mailbox",
                table: "mail_messages",
                columns: new[] { "folder_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_internet_message_id",
                schema: "mailbox",
                table: "mail_messages",
                column: "internet_message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_messages",
                schema: "mailbox");

            migrationBuilder.DropTable(
                name: "mail_folders",
                schema: "mailbox");

            migrationBuilder.DropColumn(
                name: "arrived_count",
                schema: "mailbox",
                table: "sync_runs");
        }
    }
}
