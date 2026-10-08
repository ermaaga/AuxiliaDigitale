using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;
using Auxilia.Infrastructure.Tenancy;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using NSubstitute;

namespace Auxilia.Infrastructure.Tests.Tenancy;

/// <summary>F32: readiness is Degraded while an active or suspended tenant is behind the newest schema migration.</summary>
public sealed class TenantSchemaHealthCheckTests
{
    private const string Latest = "20261008110228_Identity_TwoFactorAndRememberMe";

    private static readonly string[] Behind = ["beta", "gamma"];

    private readonly ICatalogStore catalog = Substitute.For<ICatalogStore>();
    private readonly ITenantSchemaInfo schema = Substitute.For<ITenantSchemaInfo>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenantsBehind_AreReported_ArchivedAndProvisioningOnesAreIgnored()
    {
        schema.LatestSchemaVersion.Returns(Latest);
        catalog.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns([
            Tenant("acme", Latest, TenantStatus.Active),
            Tenant("beta", "20261001000000_Old", TenantStatus.Active),
            Tenant("gamma", null, TenantStatus.Suspended),
            Tenant("delta", "20261001000000_Old", TenantStatus.Archived),
            Tenant("omega", null, TenantStatus.Provisioning),
        ]);

        var result = await Check().CheckHealthAsync(new HealthCheckContext(), Ct);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldBe($"2 tenant(s) behind schema {Latest}: beta, gamma");
        result.Data["tenantsBehind"].ShouldBe(Behind);
    }

    [Fact]
    public async Task EveryTenantCurrent_IsHealthy()
    {
        schema.LatestSchemaVersion.Returns(Latest);
        catalog.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns([Tenant("acme", Latest, TenantStatus.Active)]);

        (await Check().CheckHealthAsync(new HealthCheckContext(), Ct)).Status.ShouldBe(HealthStatus.Healthy);
    }

    private TenantSchemaHealthCheck Check() => new(catalog, schema);

    private static Tenant Tenant(string slug, string? schemaVersion, TenantStatus status)
    {
        var tenant = Domain.Platform.Tenant.Create(Guid.CreateVersion7(), slug, slug, "it", "Europe/Rome").Value;
        tenant.SetVersions(schemaVersion, null);
        if (status != TenantStatus.Provisioning)
        {
            tenant.Activate();
        }

        if (status == TenantStatus.Suspended)
        {
            tenant.Suspend();
        }
        else if (status == TenantStatus.Archived)
        {
            tenant.Archive(DateTimeOffset.UtcNow);
        }

        return tenant;
    }
}
