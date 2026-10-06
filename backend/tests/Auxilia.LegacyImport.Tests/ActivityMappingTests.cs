using Auxilia.Domain.Imports;
using Auxilia.Domain.Scheduling;
using Auxilia.MigrationRunner.LegacyImport.Steps;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The value rules of E-05 (docs/migration/mapping.md §6), without databases.</summary>
public sealed class ActivityMappingTests
{
    [Theory]
    [InlineData("Pending", AppointmentStatus.Pending)]
    [InlineData("Concluded", AppointmentStatus.Completed)]
    [InlineData("Cancelled", AppointmentStatus.Cancelled)]
    [InlineData("Postponed", null)]
    public void AppointmentStatus_MapsTheLegacyText(string legacy, AppointmentStatus? expected) => AppointmentsStep.Status(legacy).ShouldBe(expected);

    [Theory]
    [InlineData("Concluded", ImportJobStatus.Completed)]
    [InlineData("Completed", ImportJobStatus.Completed)]
    [InlineData("Failed", ImportJobStatus.Failed)]
    [InlineData("Running", ImportJobStatus.Cancelled)]
    [InlineData("Pending", ImportJobStatus.Cancelled)]
    public void ImportStatus_IsAlwaysFinished(string legacy, ImportJobStatus expected) => ImportHistoryStep.Status(legacy).ShouldBe(expected);

    [Theory]
    [InlineData("Subscription", "Case")]
    [InlineData("Membership", "Service")]
    [InlineData("Client", "Client")]
    [InlineData("Employee", "Employee")]
    public void ImportTarget_UsesTheNewEntityNames(string legacy, string expected) => ImportHistoryStep.TargetEntity(legacy).ShouldBe(expected);

    [Fact]
    public void Fit_TrimsAndCuts()
    {
        LegacyText.Fit("  abcdef ", 3, out var cut).ShouldBe("abc");
        cut.ShouldBeTrue();
        LegacyText.Fit(null, 3, out cut).ShouldBe(string.Empty);
        cut.ShouldBeFalse();
    }
}
