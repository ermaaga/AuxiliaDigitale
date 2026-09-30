using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auxilia.Persistence.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class Catalog_SingleActiveSigningKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_signing_keys_active",
                schema: "catalog",
                table: "signing_keys");

            // Nodes may have created several active keys at the same time: keep the newest, retire the others with
            // the usual validation grace (2 h), so tokens they signed stay valid.
            migrationBuilder.Sql("""
                UPDATE catalog.signing_keys
                SET retired_at = now(), published_until = now() + interval '2 hours'
                WHERE retired_at IS NULL
                  AND id <> (SELECT id FROM catalog.signing_keys WHERE retired_at IS NULL ORDER BY created_at DESC, id LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "ux_signing_keys_active",
                schema: "catalog",
                table: "signing_keys",
                column: "retired_at",
                unique: true,
                filter: "retired_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_signing_keys_active",
                schema: "catalog",
                table: "signing_keys");

            migrationBuilder.CreateIndex(
                name: "ux_signing_keys_active",
                schema: "catalog",
                table: "signing_keys",
                column: "retired_at",
                unique: true,
                filter: "retired_at IS NULL");
        }
    }
}
