using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Paging;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Cases;
using Auxilia.Application.Tests.Platform.Modules;
using Auxilia.Domain.Platform;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auxilia.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ResolvesRunnerDefaultsAndValidators()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOperationRunner>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().ActorType.ShouldBe(ActorType.System);
        scope.ServiceProvider.GetRequiredService<IValidator<PageRequest>>().ShouldBeOfType<PageRequestValidator>();
    }

    [Fact]
    public void AddApplication_KeepsHostCurrentUser()
    {
        var services = new ServiceCollection();
        var hostUser = NSubstitute.Substitute.For<ICurrentUser>();
        services.AddScoped(_ => hostUser);

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().ShouldBeSameAs(hostUser);
    }

    [Fact]
    public void AddApplication_RegistersTheCoreSettingDefinitionsOnce()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ISettingDefinitionRegistry>();

        registry.Find("cases.expiry.expiringDays").ShouldBeSameAs(CasesSettings.ExpiryExpiringDays);
        registry.All.Select(definition => definition.Key).ShouldBe(
        [
            "auth.accessToken.minutes", "auth.activation.linkHours", "auth.appBaseUrl", "auth.lockout.maxFailedAttempts",
            "auth.lockout.minutes", "auth.mfa.requiredRoles", "auth.otp.codeMinutes", "auth.otp.enabled", "auth.password.expiryEnabled",
            "auth.password.expiryMonths", "auth.password.historyCount", "auth.password.minLength", "auth.password.requireDigit",
            "auth.password.requireLowercase", "auth.password.requireSpecial", "auth.password.requireUppercase", "auth.passwordReset.linkMinutes",
            "auth.session.absoluteDays", "auth.session.idleMinutes", "auth.session.rememberMeDays", "auth.singleSession", "branding.appName", "branding.background.color",
            "branding.background.endColor", "branding.background.kind", "branding.background.startColor", "branding.theme.accentColor",
            "branding.theme.fill", "branding.theme.primaryColor", "branding.useAppName", "cases.expiry.enabled",
            "cases.expiry.expiringDays", "documents.maxUploadMb", "documents.storage.azure.connectionString", "documents.storage.azure.container", "documents.storage.ftp.host", "documents.storage.ftp.password", "documents.storage.ftp.path", "documents.storage.ftp.port", "documents.storage.ftp.tls", "documents.storage.ftp.user", "documents.storage.provider", "registration.defaultLanguage",
            "registration.enabled", "registration.minimumAge", "registration.notifyAdmins", "registration.sendConfirmationEmail",
        ]);
    }

    [Fact]
    public void AddApplication_RegistersEveryModuleOnceWithItsServices()
    {
        var services = new ServiceCollection();
        var extra = new TestModule("testing", rangeStart: 99000);
        services.AddApplication();
        services.AddModules([extra, extra]);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IModuleRegistry>();

        registry.All.Select(module => module.Code).ShouldBe(
        [
            "cases", "configuration", "directory", "documents", "engagement", "identity", "imports", "localization", "marketing",
            "messaging", "reporting", "scheduling", "testing",
        ]);
        extra.ServicesAdded.ShouldBe(1);
        registry.All.Where(module => module.Kind == ModuleKind.Core).Select(module => module.Code)
            .ShouldBe(["configuration", "identity", "imports", "localization", "messaging"]);
    }
}
