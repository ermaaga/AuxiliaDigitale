using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Cases_Cases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_numbers",
                schema: "cases",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_numbers", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specialization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_rejected = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_cases", x => x.id);
                    table.CheckConstraint("ck_cases_price", "price >= 0");
                    table.CheckConstraint("ck_cases_status", "status IN ('Inserted', 'InProgress', 'Sent', 'Completed')");
                    table.ForeignKey(
                        name: "fk_cases_client_profile_client_id",
                        column: x => x.client_id,
                        principalSchema: "directory",
                        principalTable: "client_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cases_service_service_id",
                        column: x => x.service_id,
                        principalSchema: "cases",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cases_specialization_specialization_id",
                        column: x => x.specialization_id,
                        principalSchema: "directory",
                        principalTable: "specializations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_payments",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_payments", x => x.id);
                    table.CheckConstraint("ck_case_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_case_payments_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_case_payments_user_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_status_history",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_status_history_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_case_status_history_user_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_case_payments_case_id",
                schema: "cases",
                table: "case_payments",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_payments_recorded_by_user_id",
                schema: "cases",
                table: "case_payments",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_status_history_case_id_sequence",
                schema: "cases",
                table: "case_status_history",
                columns: new[] { "case_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_status_history_changed_by_user_id",
                schema: "cases",
                table: "case_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cases_client_id",
                schema: "cases",
                table: "cases",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_cases_number",
                schema: "cases",
                table: "cases",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cases_service_id",
                schema: "cases",
                table: "cases",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_cases_specialization_id",
                schema: "cases",
                table: "cases",
                column: "specialization_id");

            migrationBuilder.CreateIndex(
                name: "ix_cases_status_started_on",
                schema: "cases",
                table: "cases",
                columns: new[] { "status", "started_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_numbers",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "case_payments",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "case_status_history",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "cases",
                schema: "cases");
        }
    }
}
