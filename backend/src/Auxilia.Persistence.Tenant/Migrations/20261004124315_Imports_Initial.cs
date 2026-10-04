using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Imports_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "imports");

            migrationBuilder.CreateTable(
                name: "import_types",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_entity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_jobs",
                schema: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_content = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    processed_rows = table.Column<int>(type: "integer", nullable: false),
                    success_rows = table.Column<int>(type: "integer", nullable: false),
                    failed_rows = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                    table.CheckConstraint("ck_import_jobs_status", "status IN ('Pending', 'Validating', 'AwaitingConfirmation', 'Processing', 'Completed', 'Failed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_import_jobs_import_type_import_type_id",
                        column: x => x.import_type_id,
                        principalSchema: "imports",
                        principalTable: "import_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_job_rows",
                schema: "imports",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    errors = table.Column<string>(type: "jsonb", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_job_rows", x => new { x.job_id, x.row_number });
                    table.CheckConstraint("ck_import_job_rows_status", "status IN ('Valid', 'Invalid', 'Imported', 'Failed')");
                    table.ForeignKey(
                        name: "fk_import_job_rows_import_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "imports",
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_job_rows_job_id_status",
                schema: "imports",
                table: "import_job_rows",
                columns: new[] { "job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_created_at",
                schema: "imports",
                table: "import_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_import_type_id",
                schema: "imports",
                table: "import_jobs",
                column: "import_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_types_name",
                schema: "imports",
                table: "import_types",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_job_rows",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "import_jobs",
                schema: "imports");

            migrationBuilder.DropTable(
                name: "import_types",
                schema: "imports");
        }
    }
}
