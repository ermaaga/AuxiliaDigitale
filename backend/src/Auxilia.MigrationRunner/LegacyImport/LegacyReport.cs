using System.Globalization;
using System.Text;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>Plain-text report of <c>auxctl legacy inspect</c> (no personal data: only names of tables and counts).</summary>
internal static class LegacyReport
{
    public static string Render(LegacyInventory inventory, IReadOnlyDictionary<string, int>? mapped)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"legacy: last migration {inventory.LastMigration ?? "-"}, Security_Update {(inventory.SecurityUpdate ? "yes" : "no")}");
        text.AppendLine(culture, $"{"table",-26} {"rows",8} {(mapped is null ? string.Empty : $"{"mapped",8} ")}{"fate",-14} {"task",-5} target");
        foreach (var (table, rows) in inventory.Tables)
        {
            var mappedColumn = mapped is null
                ? string.Empty
                : $"{(table.Disposition == LegacyDisposition.Excluded ? "-" : mapped.GetValueOrDefault(table.Name).ToString(culture)),8} ";
            var target = table.Note.Length == 0 ? table.Target : $"{table.Target} ({table.Note})";
            text.AppendLine(culture,
                $"{table.Name,-26} {(rows is { } count ? count.ToString(culture) : "absent"),8} {mappedColumn}{Fate(table.Disposition),-14} {(table.Task.Length == 0 ? "-" : table.Task),-5} {target}");
        }

        text.AppendLine(culture, $"users not migrated (SystemConfigurator only, D-18): {inventory.SystemUsers}; users without a role: {inventory.UsersWithoutRole}");
        text.AppendLine(culture, $"unknown tables (review docs/migration/mapping.md): {(inventory.UnknownTables.Count == 0 ? "none" : string.Join(", ", inventory.UnknownTables))}");
        return text.ToString();
    }

    private static string Fate(LegacyDisposition disposition) => disposition switch
    {
        LegacyDisposition.Migrated => "migrated",
        LegacyDisposition.Configuration => "configuration",
        _ => "excluded",
    };
}
