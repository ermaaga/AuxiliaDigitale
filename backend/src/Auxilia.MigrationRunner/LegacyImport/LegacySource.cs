using System.Net.Sockets;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Npgsql;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// The legacy PostgreSQL database, opened read-only (every transaction of the session is <c>read only</c>) and checked
/// against the supported baseline before anything reads it.
/// </summary>
internal sealed class LegacySource : IAsyncDisposable
{
    /// <summary>Environment variable with the legacy connection string (never on the command line).</summary>
    public const string ConnectionVariable = "AUXILIA_LEGACY_CONNECTION";

    private readonly NpgsqlDataSource dataSource;

    private LegacySource(NpgsqlDataSource dataSource, LegacySchema schema)
    {
        this.dataSource = dataSource;
        Schema = schema;
    }

    public LegacySchema Schema { get; }

    public static async Task<Result<LegacySource>> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Options = "-c default_transaction_read_only=on",
            ApplicationName = "auxctl legacy",
        };
        var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        LegacySchema schema;
        try
        {
            schema = await LegacySchema.ReadAsync(dataSource, cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or SocketException or TimeoutException)
        {
            await dataSource.DisposeAsync();
            return Errors.Runner.LegacySourceUnavailable();
        }

        var source = new LegacySource(dataSource, schema);
        await using var context = source.CreateContext();
        var missing = schema.MissingFor(context.Model);
        if (missing.Count > 0)
        {
            await source.DisposeAsync();
            return Errors.Runner.LegacySchemaUnsupported(string.Join(", ", missing));
        }

        return source;
    }

    public LegacyDbContext CreateContext() => new(LegacyDbContext.Options(dataSource), Schema.Variant);

    /// <summary>Rows of a table of <see cref="LegacyTables"/>; <c>null</c> when the database does not have it.</summary>
    public async Task<long?> CountAsync(LegacyTable table, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!Schema.HasTable(table.Name))
        {
            return null;
        }

        // The name comes from the fixed catalog, never from input.
#pragma warning disable CA2100
        await using var command = dataSource.CreateCommand($"select count(*) from \"{table.Name}\"");
#pragma warning restore CA2100
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}
