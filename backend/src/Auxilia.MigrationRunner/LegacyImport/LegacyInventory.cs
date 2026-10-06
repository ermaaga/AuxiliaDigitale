using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <param name="Rows"><c>null</c> when the database does not have the table (Security_Update tables).</param>
internal sealed record LegacyTableCount(LegacyTable Table, long? Rows);

/// <summary>What <c>auxctl legacy inspect</c> reports: schema variant, rows per table and the users left out.</summary>
/// <param name="SystemUsers">Users whose only role is SystemConfigurator (the seeded <c>system</c> user): not migrated (D-18).</param>
/// <param name="UsersWithoutRole">Users without any role: the import cannot tell clients from staff and reports them.</param>
internal sealed record LegacyInventory(
    string? LastMigration,
    bool SecurityUpdate,
    IReadOnlyList<LegacyTableCount> Tables,
    IReadOnlyList<string> UnknownTables,
    int SystemUsers,
    int UsersWithoutRole)
{
    public const string SystemConfiguratorRole = "SystemConfigurator";

    public long TotalRows => Tables.Sum(table => table.Rows ?? 0);

    public static async Task<LegacyInventory> ReadAsync(LegacySource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tables = new List<LegacyTableCount>();
        foreach (var table in LegacyTables.All)
        {
            tables.Add(new LegacyTableCount(table, await source.CountAsync(table, cancellationToken)));
        }

        await using var db = source.CreateContext();
        var roles = from userRole in db.UserRoles
                    join role in db.Roles on userRole.RoleId equals role.Id
                    select new { userRole.UserId, role.Name };
        var systemUsers = await db.Users.CountAsync(
            user => roles.Any(role => role.UserId == user.Id)
                && roles.Where(role => role.UserId == user.Id).All(role => role.Name == SystemConfiguratorRole),
            cancellationToken);
        var withoutRole = await db.Users.CountAsync(user => !roles.Any(role => role.UserId == user.Id), cancellationToken);

        return new LegacyInventory(
            source.Schema.LastMigration, source.Schema.HasSecurityUpdate, tables, source.Schema.Unknown, systemUsers, withoutRole);
    }
}
