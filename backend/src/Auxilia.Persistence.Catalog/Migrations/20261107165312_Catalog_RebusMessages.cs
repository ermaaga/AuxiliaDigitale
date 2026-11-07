using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Catalog.Migrations
{
    /// <summary>
    /// Queues of <c>Messaging:Mode = InProcess</c> (ADR 0028): the Rebus PostgreSQL transport table, owned by Rebus and not
    /// mapped in the model. Created here and not by Rebus at the Api's start because on Azure Database for PostgreSQL the
    /// Catalog login may not create in <c>public</c> (see <c>Catalog_RebusTimeouts</c>); Rebus finds the table in
    /// <c>catalog</c> and creates nothing. The DDL is the one of Rebus.PostgreSql's <c>PostgreSqlTransport</c>. Harmless
    /// in <c>Worker</c> mode (the table stays empty).
    /// </summary>
    public partial class Catalog_RebusMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS catalog.rebus_messages (
                    id SERIAL NOT NULL,
                    recipient TEXT NOT NULL,
                    priority INTEGER NOT NULL,
                    expiration TIMESTAMP WITH TIME ZONE NOT NULL,
                    visible TIMESTAMP WITH TIME ZONE NOT NULL,
                    headers BYTEA NOT NULL,
                    body BYTEA NOT NULL,
                    CONSTRAINT rebus_messages_pkey PRIMARY KEY (recipient, priority, id)
                );
                CREATE INDEX IF NOT EXISTS idx_receive_rebus_messages ON catalog.rebus_messages (recipient, expiration, visible);
                CREATE INDEX IF NOT EXISTS idx_expiration_rebus_messages ON catalog.rebus_messages (expiration);
                CREATE INDEX IF NOT EXISTS idx_dequeue_rebus_messages ON catalog.rebus_messages (priority DESC, visible, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS catalog.rebus_messages;");
        }
    }
}
