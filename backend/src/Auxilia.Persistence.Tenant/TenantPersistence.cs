using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Persistence.Tenant.Administration;
using Auxilia.Persistence.Tenant.Bus;
using Auxilia.Persistence.Tenant.Configuration;
using Auxilia.Persistence.Tenant.DataMigrations;
using Auxilia.Persistence.Tenant.Identity;
using Auxilia.Persistence.Tenant.Jobs;
using Auxilia.Persistence.Tenant.Localization;
using Auxilia.Persistence.Tenant.Messaging;
using Auxilia.Persistence.Tenant.Seed;
using Auxilia.Persistence.Tenant.Transactions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Persistence.Tenant;

public static class TenantPersistence
{
    /// <summary>
    /// Tenant databases through <see cref="ITenantDbContextFactory"/> only (never a DbContext in DI), the operation
    /// transaction used by <see cref="IOperationRunner"/>, the EF Core exception classifier and the data-migrations.
    /// </summary>
    public static IServiceCollection AddTenantPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TenantDataSources>();
        services.AddScoped<TenantOperationTransactions>();
        services.Replace(ServiceDescriptor.Scoped<IOperationTransactionFactory>(provider => provider.GetRequiredService<TenantOperationTransactions>()));
        services.AddScoped<TenantDbContextFactory>();
        services.AddScoped<ITenantDbContextFactory>(provider => provider.GetRequiredService<TenantDbContextFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, EfCoreExceptionClassifier>());

        services.AddSingleton(_ => DataMigrationRunner.Discover(typeof(TenantDbContext).Assembly));
        services.AddSingleton(provider => new DataMigrationRunner(
            provider.GetRequiredService<IReadOnlyList<IDataMigration>>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DataMigrationRunner>>()));
        services.AddSingleton<TenantInitialSeed>();
        services.AddScoped<IJobRunStore, JobRunStore>();
        services.AddScoped<ITenantSettingStore, TenantSettingStore>();
        services.AddScoped<Application.Abstractions.Configuration.IBrandingAssetStore, BrandingAssetStore>();
        services.AddScoped<Application.Abstractions.Configuration.ICustomizationDataFactory, CustomizationDataFactory>();
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore>();
        services.AddScoped<IJobLock, PostgresJobLock>();
        services.AddScoped<IMessagingDataFactory, MessagingDataFactory>();
        services.AddScoped<IIdentityDataFactory, IdentityDataFactory>();
        services.AddScoped<ISessionDataFactory, SessionDataFactory>();
        services.AddScoped<IProfileDataFactory, ProfileDataFactory>();
        services.AddScoped<IRolePermissionReader, RolePermissionReader>();
        services.AddScoped<IRolePermissionDataFactory, RolePermissionDataFactory>();
        services.AddScoped<Application.Abstractions.Directory.ISpecializationDataFactory, Directory.SpecializationDataFactory>();
        services.AddScoped<Application.Abstractions.Directory.IClientDataFactory, Directory.ClientDataFactory>();
        services.AddScoped<Application.Abstractions.Directory.IEmployeeDataFactory, Directory.EmployeeDataFactory>();
        services.AddScoped<Application.Abstractions.Directory.IRegistrationDataFactory, Directory.RegistrationDataFactory>();
        services.AddScoped<Application.Abstractions.Cases.IServiceCatalogDataFactory, Cases.ServiceCatalogDataFactory>();
        services.AddScoped<Application.Abstractions.Cases.ICaseDataFactory, Cases.CaseDataFactory>();
        services.AddScoped<Application.Abstractions.Documents.IDocumentDataFactory, Documents.DocumentDataFactory>();
        services.AddScoped<Application.Abstractions.Scheduling.IAppointmentDataFactory, Scheduling.AppointmentDataFactory>();
        services.AddScoped<Application.Abstractions.Engagement.IRequestDataFactory, Engagement.RequestDataFactory>();
        services.AddScoped<ILoginAttemptReader, LoginAttemptReader>();
        services.AddScoped<ILocalizationDataFactory, LocalizationDataFactory>();
        services.AddScoped<ILocalizationReader, LocalizationReader>();
        services.AddSingleton<PermissionSynchronizer>();

        return services;
    }

    /// <summary>Creation and migration of tenant databases (auxctl, provisioning handler): needs a CREATEDB/CREATEROLE login.</summary>
    public static IServiceCollection AddTenantDatabaseAdministration(this IServiceCollection services, string adminConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminConnectionString);

        services.AddSingleton(new TenantDatabaseAdminOptions(adminConnectionString));
        services.AddScoped<ITenantDatabaseAdmin, TenantDatabaseAdmin>();

        return services;
    }
}
