using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Platform.Modules;
using Auxilia.Domain.Platform;

using static Auxilia.Application.Tests.Platform.Modules.Roles;

namespace Auxilia.Application.Tests.Platform.Modules;

public sealed class ModuleRegistryAndVisibilityTests
{
    [Fact]
    public void Registry_OrdersByCodeAndFindsModules()
    {
        var cases = new TestModule("cases", rangeStart: 14000);
        var registry = new ModuleRegistry([new TestModule("identity", ModuleKind.Core, 12000), cases, cases]);

        registry.All.Select(module => module.Code).ShouldBe(["cases", "identity"]);
        registry.Find("cases").ShouldBeSameAs(cases);
        registry.Find("marketing").ShouldBeNull();
    }

    [Theory]
    [InlineData("Cases")]
    [InlineData("case-files")]
    [InlineData("")]
    public void Registry_InvalidCode_Throws(string code) =>
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule(code)]));

    [Fact]
    public void Registry_DuplicateCodeOrRange_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("cases", rangeStart: 1), new TestModule("cases", rangeStart: 2)]))
            .Message.ShouldContain("twice");
        Should.Throw<InvalidOperationException>(() => new ModuleRegistry([new TestModule("cases", rangeStart: 1), new TestModule("marketing", rangeStart: 1)]))
            .Message.ShouldContain("share event code range");
    }

    [Fact]
    public void Visibility_CoreForEveryRole_OverrideBeforePlan()
    {
        var tenantId = Guid.CreateVersion7();
        var source = new TenantModuleSource(
            [
                new CatalogModule("identity", ModuleKind.Core),
                new CatalogModule("cases", ModuleKind.Optional),
                new CatalogModule("marketing", ModuleKind.Optional),
                new CatalogModule("documents", ModuleKind.Optional),
                new CatalogModule("scheduling", ModuleKind.Optional),
            ],
            new Dictionary<string, TenantRole[]>
            {
                ["cases"] = [Client, Admin, Employee],
                ["marketing"] = [Admin, Employee],
                ["documents"] = [Admin],
                ["unknown"] = [Admin],
            },
            [
                new TenantModuleOverride(tenantId, "marketing", isEnabled: false, [Admin]),
                new TenantModuleOverride(tenantId, "scheduling", isEnabled: true, [Client, Employee]),
                new TenantModuleOverride(tenantId, "identity", isEnabled: false, []),
            ]);

        var modules = ModuleVisibility.Compute(source);

        modules.Modules.Keys.ShouldBe(["identity", "cases", "documents", "scheduling"], ignoreOrder: true);
        modules.Modules["identity"].ShouldBe([Admin, Employee, Client]);
        modules.Modules["cases"].ShouldBe([Admin, Employee, Client]);
        modules.Modules["scheduling"].ShouldBe([Employee, Client]);
        modules.IsVisible("documents", [Employee, Admin]).ShouldBeTrue();
        modules.IsVisible("documents", [Employee]).ShouldBeFalse();
        modules.IsVisible("marketing", [Admin]).ShouldBeFalse();
        modules.IsVisible("unknown", [Admin]).ShouldBeFalse();
    }
}
