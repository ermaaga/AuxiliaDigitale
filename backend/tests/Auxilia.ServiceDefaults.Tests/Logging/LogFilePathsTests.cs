using Auxilia.ServiceDefaults.Logging;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class LogFilePathsTests
{
    [Fact]
    public void For_TenantEvent_UsesTenantDailyFile()
    {
        LogFilePaths.For(LogEvents.Create(tenant: "acme")).ShouldBe("tenants/acme/2026/09/29.jsonl");
    }

    [Fact]
    public void For_EventWithoutTenant_UsesPlatformDailyFile()
    {
        LogFilePaths.For(LogEvents.Create()).ShouldBe("platform/2026/09/29.jsonl");
    }

    [Fact]
    public void For_NonUtcTimestamp_UsesTheUtcDay()
    {
        var lateEveningInRome = new DateTimeOffset(2026, 9, 30, 1, 30, 0, TimeSpan.FromHours(2));

        LogFilePaths.For("acme", lateEveningInRome).ShouldBe("tenants/acme/2026/09/29.jsonl");
    }

    [Theory]
    [InlineData("../acme")]
    [InlineData("acme/../../etc")]
    [InlineData("Acme")]
    [InlineData("a")]
    [InlineData("-acme")]
    [InlineData("")]
    public void For_InvalidSlug_UsesPlatformFile(string tenant)
    {
        LogFilePaths.For(tenant, LogEvents.Noon).ShouldBe("platform/2026/09/29.jsonl");
    }
}
