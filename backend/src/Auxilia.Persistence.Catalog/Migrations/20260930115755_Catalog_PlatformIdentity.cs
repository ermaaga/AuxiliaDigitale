using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Catalog_PlatformIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_totp_step",
                schema: "catalog",
                table: "platform_users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lockout_count",
                schema: "catalog",
                table: "platform_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "pending_two_factor_secret",
                schema: "catalog",
                table: "platform_users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                schema: "catalog",
                table: "platform_users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Existing platform users get their own stamp (sessions compare it).
            migrationBuilder.Sql("UPDATE catalog.platform_users SET security_stamp = replace(gen_random_uuid()::text, '-', '') WHERE security_stamp = '';");

            migrationBuilder.CreateTable(
                name: "platform_sessions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_sessions_platform_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "catalog",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_tokens",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_tokens_platform_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "catalog",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_refresh_tokens",
                schema: "catalog",
                columns: table => new
                {
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_refresh_tokens", x => x.token_hash);
                    table.ForeignKey(
                        name: "fk_platform_refresh_tokens_platform_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "catalog",
                        principalTable: "platform_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_refresh_tokens_session_id",
                schema: "catalog",
                table: "platform_refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_user_id_ended_at",
                schema: "catalog",
                table: "platform_sessions",
                columns: new[] { "user_id", "ended_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_tokens_token_hash",
                schema: "catalog",
                table: "platform_user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_tokens_user_id_purpose",
                schema: "catalog",
                table: "platform_user_tokens",
                columns: new[] { "user_id", "purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_refresh_tokens",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_user_tokens",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_sessions",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "last_totp_step",
                schema: "catalog",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "lockout_count",
                schema: "catalog",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_secret",
                schema: "catalog",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                schema: "catalog",
                table: "platform_users");
        }
    }
}
