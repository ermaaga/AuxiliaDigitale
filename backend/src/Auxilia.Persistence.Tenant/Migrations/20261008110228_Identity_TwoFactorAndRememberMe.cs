using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Identity_TwoFactorAndRememberMe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_totp_step",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_two_factor_secret",
                schema: "identity",
                table: "users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_enabled_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_secret",
                schema: "identity",
                table: "users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_remembered",
                schema: "identity",
                table: "refresh_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_totp_step",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_secret",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_remembered",
                schema: "identity",
                table: "refresh_sessions");
        }
    }
}
