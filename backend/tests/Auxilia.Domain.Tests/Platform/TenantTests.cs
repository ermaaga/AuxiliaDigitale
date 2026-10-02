using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Tests.Platform;

public sealed class TenantTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("studio-rossi-2")]
    [InlineData("abc")]
    public void Create_ValidSlug_StartsProvisioning(string slug)
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), slug, " Acme ", "it", "Europe/Rome").Value;

        tenant.Status.ShouldBe(TenantStatus.Provisioning);
        tenant.DisplayName.ShouldBe("Acme");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("Acme")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac_me")]
    [InlineData("a-very-long-slug-that-goes-beyond-forty-c")]
    public void Create_InvalidSlug_ReturnsSlugError(string slug)
    {
        var result = Tenant.Create(Guid.CreateVersion7(), slug, "Acme", "it", "Europe/Rome");

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantSlugInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData("", "it", "Europe/Rome", "displayName")]
    [InlineData("Acme", "", "Europe/Rome", "defaultLanguage")]
    [InlineData("Acme", "it", "", "timeZone")]
    public void Create_MissingValue_ReturnsFieldError(string name, string language, string timeZone, string field)
    {
        var result = Tenant.Create(Guid.CreateVersion7(), "acme", name, language, timeZone);

        result.Error!.ValidationErrors.Keys.ShouldBe([field]);
    }

    [Fact]
    public void Lifecycle_ProvisionSuspendReactivateArchive()
    {
        var tenant = NewTenant();
        var archivedAt = DateTimeOffset.UtcNow;

        tenant.Activate().IsSuccess.ShouldBeTrue();
        tenant.Suspend().IsSuccess.ShouldBeTrue();
        tenant.Activate().IsSuccess.ShouldBeTrue();
        tenant.Archive(archivedAt).IsSuccess.ShouldBeTrue();

        tenant.Status.ShouldBe(TenantStatus.Archived);
        tenant.ArchivedAt.ShouldBe(archivedAt);
    }

    [Fact]
    public void Archived_IsTerminal()
    {
        var tenant = NewTenant();
        tenant.Archive(DateTimeOffset.UtcNow);

        var result = tenant.Activate();

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.TenantTransitionNotAllowed);
        tenant.Status.ShouldBe(TenantStatus.Archived);
    }

    [Fact]
    public void Suspend_WhileProvisioning_IsNotAllowed()
    {
        NewTenant().Suspend().Error!.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void MigrationFailed_CanRecoverToActive()
    {
        var tenant = NewTenant();

        tenant.MarkMigrationFailed().IsSuccess.ShouldBeTrue();
        tenant.Activate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Rename_And_Secrets()
    {
        var tenant = NewTenant();

        tenant.Rename("").IsFailure.ShouldBeTrue();
        tenant.Rename("New name").IsSuccess.ShouldBeTrue();
        tenant.SetConnectionSecret("CfDJ8protected");
        tenant.SetVersions("Catalog_Initial", "D_20260929_001");

        tenant.DisplayName.ShouldBe("New name");
        tenant.ConnectionSecret.ShouldBe("CfDJ8protected");
        tenant.SchemaVersion.ShouldBe("Catalog_Initial");
        tenant.DataVersion.ShouldBe("D_20260929_001");
    }

    private static Tenant NewTenant() => Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;

    [Fact]
    public void Update_ChangesNameAndTimeZone_ButNotOfAnArchivedTenant()
    {
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;

        tenant.Update(" ACME Group ", "Europe/London").IsSuccess.ShouldBeTrue();
        (tenant.DisplayName, tenant.TimeZone).ShouldBe(("ACME Group", "Europe/London"));
        tenant.Update("", "Europe/Rome").IsFailure.ShouldBeTrue();
        tenant.Update("Acme", new string('x', Tenant.TimeZoneMaxLength + 1)).Error!.Code.ShouldBe(EventCodes.Tenancy.CatalogValueInvalid);
        tenant.TimeZone.ShouldBe("Europe/London");

        tenant.Archive(DateTimeOffset.UnixEpoch);
        tenant.Update("Other", "Europe/Rome").Error!.Code.ShouldBe(EventCodes.Tenancy.TenantArchived);

        // "new" is a console path (/platform/tenants/new), never a tenant.
        Tenant.Create(Guid.CreateVersion7(), "new", "New", "it", "Europe/Rome").Error!.Code.ShouldBe(EventCodes.Tenancy.TenantSlugReserved);
        tenant.DisplayName.ShouldBe("ACME Group");
    }

    [Fact]
    public void EnableDebugLogging_WithinTheNext24Hours_RunsUntilTheEnd()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;

        tenant.EnableDebugLogging(now.AddHours(2), now).IsSuccess.ShouldBeTrue();

        tenant.DebugLoggingUntil.ShouldBe(now.AddHours(2));
        tenant.ActiveDebugLoggingUntil(now.AddHours(1)).ShouldBe(now.AddHours(2));
        tenant.ActiveDebugLoggingUntil(now.AddHours(2)).ShouldBeNull();
        tenant.EnableDebugLogging(now.Add(Tenant.MaxDebugLogging), now).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(24 * 60 + 1)]
    public void EnableDebugLogging_EndNotInTheNext24Hours_IsRejected(int minutes)
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;

        var result = tenant.EnableDebugLogging(now.AddMinutes(minutes), now);

        result.Error!.Code.ShouldBe(EventCodes.Tenancy.LogLevelUntilInvalid);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        tenant.DebugLoggingUntil.ShouldBeNull();
    }

    [Fact]
    public void DebugLogging_ArchivedTenant_CannotEnableButCanDisable()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create(Guid.CreateVersion7(), "acme", "Acme", "it", "Europe/Rome").Value;
        tenant.EnableDebugLogging(now.AddHours(1), now);
        tenant.Archive(now);

        tenant.EnableDebugLogging(now.AddHours(2), now).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantArchived);
        tenant.DisableDebugLogging();

        tenant.DebugLoggingUntil.ShouldBeNull();
    }
}
