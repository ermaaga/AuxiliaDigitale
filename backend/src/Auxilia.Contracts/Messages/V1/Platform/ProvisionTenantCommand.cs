namespace Auxilia.Contracts.Messages.V1.Platform;

/// <summary>
/// Provisions (or resumes) the tenant created by the System in the console (N02). Not a tenant message: the tenant
/// has no database yet. With <see cref="Administrator"/> the first Administrator is created and invited afterwards.
/// </summary>
public sealed record ProvisionTenantCommand(string Slug, ProvisionTenantAdministrator? Administrator);

public sealed record ProvisionTenantAdministrator(string Email, string FirstName, string LastName);
