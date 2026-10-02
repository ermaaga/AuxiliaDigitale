using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Directory_Clients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                schema: "directory",
                table: "people",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_fields",
                schema: "directory",
                table: "people",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "fiscal_code",
                schema: "directory",
                table: "people",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "directory",
                table: "people",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "client_profiles",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    employee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_profiles", x => x.id);
                    table.CheckConstraint("ck_client_profiles_status", "status IN ('Inactive', 'Active')");
                    table.ForeignKey(
                        name: "fk_client_profiles_person_id",
                        column: x => x.id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_client_profiles_user_employee_user_id",
                        column: x => x.employee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_assignments",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_client_assignments_client_profiles_client_id",
                        column: x => x.client_id,
                        principalSchema: "directory",
                        principalTable: "client_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_assignments_user_employee_user_id",
                        column: x => x.employee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_people_custom_fields",
                schema: "directory",
                table: "people",
                column: "custom_fields")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_people_fiscal_code",
                schema: "directory",
                table: "people",
                column: "fiscal_code",
                unique: true,
                filter: "fiscal_code IS NOT NULL AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignments_client_id",
                schema: "directory",
                table: "client_assignments",
                column: "client_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_client_assignments_employee_user_id",
                schema: "directory",
                table: "client_assignments",
                column: "employee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_profiles_employee_user_id",
                schema: "directory",
                table: "client_profiles",
                column: "employee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_profiles_status",
                schema: "directory",
                table: "client_profiles",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_assignments",
                schema: "directory");

            migrationBuilder.DropTable(
                name: "client_profiles",
                schema: "directory");

            migrationBuilder.DropIndex(
                name: "ix_people_custom_fields",
                schema: "directory",
                table: "people");

            migrationBuilder.DropIndex(
                name: "ix_people_fiscal_code",
                schema: "directory",
                table: "people");

            migrationBuilder.DropColumn(
                name: "birth_date",
                schema: "directory",
                table: "people");

            migrationBuilder.DropColumn(
                name: "custom_fields",
                schema: "directory",
                table: "people");

            migrationBuilder.DropColumn(
                name: "fiscal_code",
                schema: "directory",
                table: "people");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "directory",
                table: "people");
        }
    }
}
