using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Platform;

/// <summary>Host name that resolves to a tenant (subdomain or custom domain), unique across tenants.</summary>
public sealed class TenantDomain : Entity<Guid>
{
    public const int HostMaxLength = 253;

    public TenantDomain(Guid id, Guid tenantId, string host)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(host.Length, HostMaxLength);

        TenantId = tenantId;
        Host = host.Trim().ToLowerInvariant();
    }

    private TenantDomain()
    {
        Host = string.Empty;
    }

    public Guid TenantId { get; private set; }

    public string Host { get; private set; }
}
