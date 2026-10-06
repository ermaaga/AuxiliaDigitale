using Auxilia.MigrationRunner.LegacyImport;

namespace Auxilia.LegacyImport.Tests;

/// <summary>How the reconciliation reads (E-06), without databases.</summary>
public sealed class ReconciliationTests
{
    [Fact]
    public void Render_ListsEveryCheck_AndBlocksOnADifference()
    {
        IReadOnlyList<ReconciliationCheck> checks = [new("users per role", "Client 2", "Client 2"), new("rejected cases", "3", "2")];

        var text = LegacyReconciliation.Render(checks);

        text.ShouldStartWith("reconciliation: 1 difference(s), the cutover is blocked");
        text.ShouldContain("ok   users per role");
        text.ShouldContain("DIFF rejected cases");
        LegacyReconciliation.Render([new("rejected cases", "3", "3")]).ShouldStartWith("reconciliation: no differences");
    }

    [Fact]
    public void MappedTables_AreMigratedTablesOfTheCatalog() =>
        LegacyReconciliation.MappedTables.Where(name => LegacyTables.All.All(table => table.Name != name || table.Disposition == LegacyDisposition.Excluded))
            .ShouldBeEmpty();

    [Fact]
    public void Report_WithAReconciliation_IsReconciledOnlyWithoutDifferences()
    {
        var report = new LegacyImportReport { Reconciliation = [new("a", "1", "1")] };
        report.Reconciled.ShouldBeTrue();

        report.Reconciliation = [new("a", "1", "2")];
        report.Reconciled.ShouldBeFalse();
        report.Render(dryRun: true).ShouldContain("DIFF a");
    }
}
