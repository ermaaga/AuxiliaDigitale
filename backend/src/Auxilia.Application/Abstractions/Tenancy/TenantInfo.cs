using Auxilia.Domain.Platform;

namespace Auxilia.Application.Abstractions.Tenancy;

/// <summary>What the application knows about the current tenant. Never carries the connection string.</summary>
public sealed record TenantInfo(Guid Id, string Slug, TenantStatus Status, string DefaultLanguage, string TimeZone)
{
    public bool IsActive => Status == TenantStatus.Active;
}
