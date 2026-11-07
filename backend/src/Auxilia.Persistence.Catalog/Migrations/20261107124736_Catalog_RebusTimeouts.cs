using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Catalog.Migrations
{
    /// <summary>
    /// Deferred messages of the Worker's second-level retries (Rebus timeout manager), owned by Rebus and not mapped in
    /// the model. The table used to be created by Rebus itself in <c>public</c> at the Worker's start, which fails on
    /// Azure Database for PostgreSQL: <c>public</c> belongs to <c>azure_pg_admin</c> there, so the Catalog login may not
    /// create in it and the Worker never started. The DDL is the one of Rebus.PostgreSql's
    /// <c>PostgreSqlTimeoutManager</c>; deferred messages already stored in <c>public.rebus_timeouts</c> are moved.
    /// </summary>
    public partial class Catalog_RebusTimeouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS catalog.rebus_timeouts (
                    "id" BIGSERIAL NOT NULL,
                    "due_time" TIMESTAMP WITH TIME ZONE NOT NULL,
                    "headers" TEXT NULL,
                    "body" BYTEA NULL,
                    PRIMARY KEY ("id")
                );
                CREATE INDEX IF NOT EXISTS ix_rebus_timeouts_due_time ON catalog.rebus_timeouts ("due_time");

                DO $$
                BEGIN
                    IF to_regclass('public.rebus_timeouts') IS NOT NULL THEN
                        INSERT INTO catalog.rebus_timeouts ("due_time", "headers", "body")
                        SELECT "due_time", "headers", "body" FROM public.rebus_timeouts;

                        IF EXISTS (SELECT 1 FROM pg_tables
                                   WHERE schemaname = 'public' AND tablename = 'rebus_timeouts' AND tableowner = current_user) THEN
                            DROP TABLE public.rebus_timeouts;
                        ELSE
                            DELETE FROM public.rebus_timeouts;
                        END IF;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS catalog.rebus_timeouts;");
        }
    }
}
