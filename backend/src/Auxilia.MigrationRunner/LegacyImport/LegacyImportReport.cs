using System.Globalization;
using System.Text;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>What happened to the rows of one legacy table in an import run.</summary>
internal sealed class LegacyTableResult
{
    /// <summary>New records.</summary>
    public int Created { get; set; }

    /// <summary>Records of an earlier run brought up to date.</summary>
    public int Updated { get; set; }

    /// <summary>Rows left out by decision (e.g. the SystemConfigurator user, D-18).</summary>
    public int Excluded { get; set; }

    /// <summary>Rows not migrated because of an issue (<see cref="LegacyImportReport.Issues"/>).</summary>
    public int Skipped { get; set; }
}

/// <summary>Severity of an import issue.</summary>
internal enum LegacyIssueKind
{
    /// <summary>The row was migrated with a change (e.g. an invalid phone left out).</summary>
    Warning,

    /// <summary>The row was not migrated.</summary>
    Skipped,
}

/// <param name="Reason">Fixed English text, never personal data (names, e-mails, fiscal codes).</param>
internal sealed record LegacyImportIssue(string Table, int LegacyId, LegacyIssueKind Kind, string Reason);

/// <summary>The outcome of <c>auxctl legacy import</c>: counts per table and the issues of single rows.</summary>
internal sealed class LegacyImportReport
{
    private const int IdsShown = 20;

    private readonly SortedDictionary<string, LegacyTableResult> tables = new(StringComparer.Ordinal);
    private readonly List<LegacyImportIssue> issues = [];

    public IReadOnlyDictionary<string, LegacyTableResult> Tables => tables;

    public IReadOnlyList<LegacyImportIssue> Issues => issues;

    /// <summary>The comparison the run ended with (E-06); any difference blocks the cutover.</summary>
    public IReadOnlyList<ReconciliationCheck> Reconciliation { get; set; } = [];

    public bool Reconciled => Reconciliation.All(check => check.Matches);

    public LegacyTableResult For(string table)
    {
        if (!tables.TryGetValue(table, out var result))
        {
            result = new LegacyTableResult();
            tables[table] = result;
        }

        return result;
    }

    public void Warn(string table, int legacyId, string reason) =>
        issues.Add(new LegacyImportIssue(table, legacyId, LegacyIssueKind.Warning, reason));

    public void Skip(string table, int legacyId, string reason)
    {
        issues.Add(new LegacyImportIssue(table, legacyId, LegacyIssueKind.Skipped, reason));
        For(table).Skipped++;
    }

    public string Render(bool dryRun)
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(dryRun ? "legacy import (dry run: nothing was saved)" : "legacy import");
        text.AppendLine(culture, $"{"table",-26} {"created",8} {"updated",8} {"excluded",8} {"skipped",8}");
        foreach (var (table, result) in tables)
        {
            text.AppendLine(culture, $"{table,-26} {result.Created,8} {result.Updated,8} {result.Excluded,8} {result.Skipped,8}");
        }

        foreach (var group in issues.GroupBy(issue => (issue.Table, issue.Kind, issue.Reason)).OrderBy(group => group.Key.Table, StringComparer.Ordinal))
        {
            var ids = group.Select(issue => issue.LegacyId).Distinct().Order().ToArray();
            var shown = string.Join(", ", ids.Take(IdsShown)) + (ids.Length > IdsShown ? ", …" : string.Empty);
            text.AppendLine(culture,
                $"{(group.Key.Kind == LegacyIssueKind.Skipped ? "skipped" : "warning")} {group.Key.Table}: {group.Key.Reason} ({ids.Length}: legacy ids {shown})");
        }

        if (Reconciliation.Count > 0)
        {
            text.Append(LegacyReconciliation.Render(Reconciliation));
        }

        return text.ToString();
    }
}
