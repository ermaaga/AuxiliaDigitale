using System.Reflection;

using Auxilia.Application.Abstractions.Tenancy;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Auxilia.Persistence.Tenant.Administration;

/// <summary>The id of the newest <see cref="TenantDbContext"/> migration in this assembly (the same id a migrated tenant records).</summary>
internal sealed class TenantSchemaInfo : ITenantSchemaInfo
{
    private static readonly Lazy<string?> Latest = new(() => typeof(TenantDbContext).Assembly.GetTypes()
        .Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(TenantDbContext))
        .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Id)
        .Where(id => id is not null)
        .Max(StringComparer.Ordinal));

    public string? LatestSchemaVersion => Latest.Value;
}
