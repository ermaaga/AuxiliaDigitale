using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auxilia.Persistence.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class Tenant_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "data_migrations_history",
                schema: "ops",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    covered_by_seed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_migrations_history", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "entity_changes",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_changes", x => x.id);
                    table.CheckConstraint("ck_entity_changes_action", "action IN ('Created', 'Updated', 'Deleted')");
                    table.CheckConstraint("ck_entity_changes_actor_type", "actor_type IN ('Anonymous', 'User', 'Platform', 'System')");
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_runs", x => x.id);
                    table.CheckConstraint("ck_job_runs_status", "status IN ('Running', 'Succeeded', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "legacy_id_map",
                schema: "ops",
                columns: table => new
                {
                    entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    legacy_id = table.Column<int>(type: "integer", nullable: false),
                    new_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_id_map", x => new { x.entity, x.legacy_id });
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "ops",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => new { x.scope, x.year });
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_changes_entity_type_entity_id_occurred_at",
                schema: "audit",
                table: "entity_changes",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_runs_job_code_started_at",
                schema: "ops",
                table: "job_runs",
                columns: new[] { "job_code", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_id_map_new_id",
                schema: "ops",
                table: "legacy_id_map",
                column: "new_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_migrations_history",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "entity_changes",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "job_runs",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "legacy_id_map",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "ops");
        }
    }
}
