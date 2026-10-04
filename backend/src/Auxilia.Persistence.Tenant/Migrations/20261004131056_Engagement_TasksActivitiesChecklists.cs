using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Engagement_TasksActivitiesChecklists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activities",
                schema: "engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                    table.CheckConstraint("ck_activities_kind", "kind IN ('Note', 'Call', 'Meeting', 'Email')");
                    table.ForeignKey(
                        name: "fk_activities_people_client_id",
                        column: x => x.client_id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activities_user_author_user_id",
                        column: x => x.author_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_checklist_items",
                schema: "cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_checklist_items_service_folder_folder_id",
                        column: x => x.folder_id,
                        principalSchema: "cases",
                        principalTable: "service_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_service_checklist_items_service_service_id",
                        column: x => x.service_id,
                        principalSchema: "cases",
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.CheckConstraint("ck_tasks_status", "status IN ('Open', 'Done')");
                    table.ForeignKey(
                        name: "fk_tasks_cases_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_people_client_id",
                        column: x => x.client_id,
                        principalSchema: "directory",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_user_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_user_completed_by_user_id",
                        column: x => x.completed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_user_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_checklist_marks",
                schema: "cases",
                columns: table => new
                {
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_checklist_marks", x => new { x.case_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_case_checklist_marks_case_case_id",
                        column: x => x.case_id,
                        principalSchema: "cases",
                        principalTable: "cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_case_checklist_marks_service_checklist_item_item_id",
                        column: x => x.item_id,
                        principalSchema: "cases",
                        principalTable: "service_checklist_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_case_checklist_marks_user_checked_by_user_id",
                        column: x => x.checked_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_author_user_id",
                schema: "engagement",
                table: "activities",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activities_client_id_occurred_at",
                schema: "engagement",
                table: "activities",
                columns: new[] { "client_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_case_checklist_marks_checked_by_user_id",
                schema: "cases",
                table: "case_checklist_marks",
                column: "checked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_checklist_marks_item_id",
                schema: "cases",
                table: "case_checklist_marks",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_checklist_items_folder_id",
                schema: "cases",
                table: "service_checklist_items",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_checklist_items_service_id_order",
                schema: "cases",
                table: "service_checklist_items",
                columns: new[] { "service_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_user_id_status_due_on",
                schema: "engagement",
                table: "tasks",
                columns: new[] { "assignee_user_id", "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_case_id",
                schema: "engagement",
                table: "tasks",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_client_id_created_at",
                schema: "engagement",
                table: "tasks",
                columns: new[] { "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_completed_by_user_id",
                schema: "engagement",
                table: "tasks",
                column: "completed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by_user_id",
                schema: "engagement",
                table: "tasks",
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "case_checklist_marks",
                schema: "cases");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "service_checklist_items",
                schema: "cases");
        }
    }
}
