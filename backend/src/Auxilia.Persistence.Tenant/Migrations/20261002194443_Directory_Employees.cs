using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Directory_Employees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_profiles",
                schema: "directory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_profiles_user_administrator_user_id",
                        column: x => x.administrator_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_profiles_user_id",
                        column: x => x.id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_profiles_administrator_user_id",
                schema: "directory",
                table: "employee_profiles",
                column: "administrator_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_employee_profiles_default",
                schema: "directory",
                table: "employee_profiles",
                column: "is_default",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_profiles",
                schema: "directory");
        }
    }
}
