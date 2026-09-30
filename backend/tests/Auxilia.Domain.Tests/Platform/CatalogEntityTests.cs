using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Tests.Platform;

public sealed class CatalogEntityTests
{
    [Fact]
    public void Plan_SetModule_ReplacesRolesAndRemovesWhenEmpty()
    {
        var plan = Plan.Create(Guid.CreateVersion7(), "pro", "platform.plans.pro", isDefault: false).Value;

        plan.SetModule("cases", [TenantRole.Employee, TenantRole.Administrator, TenantRole.Employee]);
        plan.RolesFor("cases").ShouldBe([TenantRole.Administrator, TenantRole.Employee]);

        plan.SetModule("cases", [TenantRole.Client]);
        plan.RolesFor("cases").ShouldBe([TenantRole.Client]);

        plan.SetModule("cases", []);
        plan.Modules.ShouldBeEmpty();
        plan.RolesFor("cases").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "platform.plans.x")]
    [InlineData("Pro", "platform.plans.x")]
    [InlineData("pro", "")]
    public void Plan_Create_InvalidValues_Fail(string code, string nameKey)
    {
        Plan.Create(Guid.CreateVersion7(), code, nameKey, isDefault: false).Error!.Code.ShouldBe(EventCodes.Tenancy.CatalogValueInvalid);
    }

    [Fact]
    public void TenantModuleOverride_Disabled_HasNoRoles()
    {
        var item = new TenantModuleOverride(Guid.CreateVersion7(), "marketing", isEnabled: true, [TenantRole.Administrator]);

        item.Set(isEnabled: false, [TenantRole.Administrator]);

        item.IsEnabled.ShouldBeFalse();
        item.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void TenantPlan_IsValidAt_RespectsPeriod()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var plan = new TenantPlan(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), start);

        plan.IsValidAt(start.AddDays(-1)).ShouldBeFalse();
        plan.IsValidAt(start).ShouldBeTrue();

        plan.End(start.AddMonths(1));
        plan.IsValidAt(start.AddMonths(1)).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => new TenantPlan(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), start, start));
    }

    [Fact]
    public void ClientApplication_NormalisesOriginsAndKnowsConfidentiality()
    {
        var web = ClientApplication.Create(Guid.CreateVersion7(), " web-bff ", "Web", ClientApplicationType.WebBff).Value;
        var mobile = ClientApplication.Create(Guid.CreateVersion7(), "mobile", "Mobile", ClientApplicationType.Mobile).Value;

        web.SetAllowedOrigins(["https://app.test/", "HTTPS://APP.TEST"]);
        web.SetSecretHash("hash");
        web.SetMinAppVersion("1.2.0");
        web.SetCaptchaProvider("altcha");
        web.Disable();

        web.ClientId.ShouldBe("web-bff");
        web.AllowedOrigins.ShouldBe(["https://app.test"]);
        web.IsConfidential.ShouldBeTrue();
        mobile.IsConfidential.ShouldBeFalse();
        web.IsEnabled.ShouldBeFalse();
        web.CaptchaProvider.ShouldBe("altcha");
        ClientApplication.Create(Guid.CreateVersion7(), "", "Web", ClientApplicationType.WebBff).IsFailure.ShouldBeTrue();
        ClientApplication.Create(Guid.CreateVersion7(), "web", "", ClientApplicationType.WebBff).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void MigrationRun_FinishesOnceAndTruncatesMessage()
    {
        var run = new MigrationRun(Guid.CreateVersion7(), null, MigrationRunKind.CatalogSchema, "Catalog_Initial", "auxctl", DateTimeOffset.UtcNow);

        run.Fail(DateTimeOffset.UtcNow, "AUX-28001", new string('x', MigrationRun.MessageMaxLength + 10));

        run.Status.ShouldBe(MigrationRunStatus.Failed);
        run.Message!.Length.ShouldBe(MigrationRun.MessageMaxLength);
        Should.Throw<InvalidOperationException>(() => run.Succeed(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PlatformUser_HasSystemRoleAndCanBeDeactivated()
    {
        var user = new PlatformUser(Guid.CreateVersion7(), " ops@auxilia.test ", "Ops");

        user.Deactivate();

        user.Email.ShouldBe("ops@auxilia.test");
        user.Roles.Single().Role.ShouldBe(PlatformUser.SystemRole);
        user.IsActive.ShouldBeFalse();
        user.Activate();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void PlatformModule_UpdateAndRetire()
    {
        var module = new PlatformModule("cases", ModuleKind.Core, "modules.cases", 14000);

        module.MarkUnavailable();
        module.IsAvailable.ShouldBeFalse();

        module.Update(ModuleKind.Optional, "modules.cases.v2", 14000);
        module.IsAvailable.ShouldBeTrue();
        module.Kind.ShouldBe(ModuleKind.Optional);
    }

    [Fact]
    public void TenantDomainAndSetting_Normalise()
    {
        new TenantDomain(Guid.CreateVersion7(), Guid.CreateVersion7(), " Acme.Auxilia.App ").Host.ShouldBe("acme.auxilia.app");

        var setting = new PlatformSetting("logging.default", "{}");
        setting.SetValue("""{"level":"Information"}""");
        setting.JsonValue.ShouldContain("Information");
    }
}
