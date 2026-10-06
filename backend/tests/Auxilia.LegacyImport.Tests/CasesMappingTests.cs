using Auxilia.Domain.Cases;
using Auxilia.MigrationRunner.LegacyImport.Steps;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The value rules of E-03 (docs/migration/mapping.md §5.2, §6), without databases.</summary>
public sealed class CasesMappingTests
{
    [Theory]
    [InlineData(0, CaseStatus.Inserted)]
    [InlineData(3, CaseStatus.Completed)]
    [InlineData(4, null)]
    public void Status_MapsTheLegacyEnum(int legacy, CaseStatus? expected) => CasesStep.Status(legacy).ShouldBe(expected);

    [Theory]
    [InlineData(null, "{}")]
    [InlineData("""{"CAF":true}""", """{"CAF":true}""")]
    [InlineData("[1]", null)]
    [InlineData("not json", null)]
    public void CustomFields_KeepOnlyJsonObjects(string? legacy, string? expected) => CasesStep.CustomFields(legacy).ShouldBe(expected);

    [Fact]
    public void UniqueName_AppendsTheLegacyIdToADuplicate()
    {
        var taken = new HashSet<string>(["Fiscale"], StringComparer.OrdinalIgnoreCase);

        ServiceCatalogStep.UniqueName(" fiscale ", 7, 100, taken, out var renamed).ShouldBe("fiscale (7)");
        renamed.ShouldBeTrue();
        ServiceCatalogStep.UniqueName("ISEE", 8, 100, taken, out renamed).ShouldBe("ISEE");
        renamed.ShouldBeFalse();
    }
}
