using Auxilia.Diagnostics.Logging;
using Auxilia.ServiceDefaults.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog.Events;

namespace Auxilia.ServiceDefaults.Tests.Logging;

public sealed class TenantLogLevelsTests
{
    private readonly ManualTimeProvider clock = new(LogEvents.Noon);
    private readonly TenantLogLevels levels;
    private readonly TenantLevelFilter filter;

    public TenantLogLevelsTests()
    {
        levels = new TenantLogLevels(LogEventLevel.Information, clock);
        filter = new TenantLevelFilter(levels);
    }

    [Fact]
    public void Default_DropsDebugForEveryone()
    {
        filter.IsEnabled(LogEvents.Create(LogEventLevel.Debug, tenant: "acme")).ShouldBeFalse();
        filter.IsEnabled(LogEvents.Create(LogEventLevel.Information, tenant: "acme")).ShouldBeTrue();
        levels.MinimumLevel.MinimumLevel.ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void SetOverride_LowersLevelOnlyForThatTenant()
    {
        levels.SetOverride("acme", LogEventLevel.Debug, LogEvents.Noon.AddHours(1));

        filter.IsEnabled(LogEvents.Create(LogEventLevel.Debug, tenant: "acme")).ShouldBeTrue();
        filter.IsEnabled(LogEvents.Create(LogEventLevel.Debug, tenant: "other")).ShouldBeFalse();
        filter.IsEnabled(LogEvents.Create(LogEventLevel.Debug)).ShouldBeFalse();
        levels.MinimumLevel.MinimumLevel.ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void Override_ExpiresByItself()
    {
        levels.SetOverride("acme", LogEventLevel.Debug, LogEvents.Noon.AddHours(1));

        clock.Now = LogEvents.Noon.AddHours(1);

        filter.IsEnabled(LogEvents.Create(LogEventLevel.Debug, tenant: "acme")).ShouldBeFalse();
        levels.MinimumLevel.MinimumLevel.ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void Override_ExpiresEvenIfOnlyOtherTenantsLog()
    {
        levels.SetOverride("acme", LogEventLevel.Verbose, LogEvents.Noon.AddMinutes(5));

        clock.Now = LogEvents.Noon.AddMinutes(10);
        filter.IsEnabled(LogEvents.Create(LogEventLevel.Information, tenant: "other"));

        levels.MinimumLevel.MinimumLevel.ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void ClearOverride_RestoresDefault()
    {
        levels.SetOverride("acme", LogEventLevel.Debug, LogEvents.Noon.AddHours(1));

        levels.ClearOverride("acme");

        levels.LevelFor("acme").ShouldBe(LogEventLevel.Information);
        levels.MinimumLevel.MinimumLevel.ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void SetOverride_AlreadyExpired_IsIgnored()
    {
        levels.SetOverride("acme", LogEventLevel.Debug, LogEvents.Noon.AddMinutes(-1));

        levels.LevelFor("acme").ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void AddAuxiliaLogging_RegistersThePipelineLevelsAsThePortAndTheReader()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration["AuxiliaLogging:Storage"] = AuxiliaLoggingOptions.NoStorage;
        builder.AddAuxiliaLogging();
        using var host = builder.Build();

        var pipeline = host.Services.GetRequiredService<TenantLogLevels>();
        var port = host.Services.GetRequiredService<ITenantLogLevels>();
        port.EnableDebug("acme", DateTimeOffset.UtcNow.AddMinutes(30));

        port.DefaultLevel.ShouldBe("Information");
        pipeline.LevelFor("acme").ShouldBe(LogEventLevel.Debug);
        port.Clear("acme");
        pipeline.LevelFor("acme").ShouldBe(LogEventLevel.Information);
        host.Services.GetRequiredService<ILogFileReader>().ShouldBeOfType<LogFileReader>();
    }
}
