using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Scheduling_Appointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    show_in_global_calendar = table.Column<bool>(type: "boolean", nullable: false),
                    requested_by_client = table.Column<bool>(type: "boolean", nullable: false),
                    custom_fields = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_duration", "duration_minutes BETWEEN 5 AND 1440");
                    table.CheckConstraint("ck_appointments_ends_at", "ends_at > starts_at");
                    table.CheckConstraint("ck_appointments_status", "status IN ('Pending', 'Approved', 'Rejected', 'Completed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_appointments_client_profiles_client_id",
                        column: x => x.client_id,
                        principalSchema: "directory",
                        principalTable: "client_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_users_employee_user_id",
                        column: x => x.employee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_history",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalSchema: "scheduling",
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_appointment_status_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_appointment_id_sequence",
                schema: "scheduling",
                table: "appointment_status_history",
                columns: new[] { "appointment_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_changed_by_user_id",
                schema: "scheduling",
                table: "appointment_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_client_id_starts_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "client_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_employee_user_id_starts_at",
                schema: "scheduling",
                table: "appointments",
                columns: new[] { "employee_user_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_starts_at",
                schema: "scheduling",
                table: "appointments",
                column: "starts_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointment_status_history",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "scheduling");
        }
    }
}
