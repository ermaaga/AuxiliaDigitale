using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Npgsql;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// The shape of a legacy database: which tables and columns it has, read from <c>information_schema</c>.
/// Supported: the develop baseline, with or without the Security_Update branch (D-30).
/// </summary>
internal sealed class LegacySchema
{
    private readonly HashSet<string> tables;
    private readonly HashSet<(string Table, string Column)> columns;

    private LegacySchema(HashSet<(string Table, string Column)> columns, string? lastMigration)
    {
        this.columns = columns;
        tables = columns.Select(column => column.Table).ToHashSet(StringComparer.Ordinal);
        LastMigration = lastMigration;
    }

    /// <summary>Latest legacy EF migration applied (<c>__EFMigrationsHistory</c>), if any.</summary>
    public string? LastMigration { get; }

    /// <summary>The database has the tables and the user column of the legacy branch Security_Update.</summary>
    public bool HasSecurityUpdate =>
        tables.Contains("PasswordHistories") && tables.Contains("LoginAuditLogs") && HasColumn("Users", "PasswordChangedAt");

    /// <summary>
    /// Baseline tables, and columns of the read model (<paramref name="model"/>), that the database lacks: the import
    /// refuses to run unless this is empty.
    /// </summary>
    public IReadOnlyList<string> MissingFor(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var missingTables = LegacyTables.Baseline.Where(table => !HasTable(table.Name)).Select(table => table.Name).ToList();
        var missingColumns =
            from entity in model.GetEntityTypes()
            let table = entity.GetTableName()!
            where HasTable(table)
            from property in entity.GetProperties()
            let column = property.GetColumnName()
            where !HasColumn(table, column)
            select $"{table}.{column}";
        return missingTables.Concat(missingColumns).ToArray();
    }

    public LegacyVariant Variant => new(HasSecurityUpdate, HasTable("ImportJobs"));

    public bool HasTable(string name) => tables.Contains(name);

    public bool HasColumn(string table, string column) => columns.Contains((table, column));

    /// <summary>Tables of the database that the import does not know (a newer legacy version: review the mapping).</summary>
    public IReadOnlyList<string> Unknown =>
        tables.Where(name => name != "__EFMigrationsHistory" && LegacyTables.All.All(table => table.Name != name)).Order(StringComparer.Ordinal).ToArray();

    public static async Task<LegacySchema> ReadAsync(NpgsqlDataSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var columns = new HashSet<(string Table, string Column)>();
        await using (var command = source.CreateCommand(
            "select table_name, column_name from information_schema.columns where table_schema = 'public'"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        string? lastMigration = null;
        if (columns.Contains(("__EFMigrationsHistory", "MigrationId")))
        {
            await using var command = source.CreateCommand("""select max("MigrationId") from "__EFMigrationsHistory" """);
            lastMigration = await command.ExecuteScalarAsync(cancellationToken) as string;
        }

        return new LegacySchema(columns, lastMigration);
    }
}
