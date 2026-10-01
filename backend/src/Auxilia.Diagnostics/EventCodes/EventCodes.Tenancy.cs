namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(11000, "Tenancy / Catalog")]
    public static class Tenancy
    {
        /// <summary>The tenant in the token differs from the tenant requested by header or host (403).</summary>
        public const int CrossTenantAttempt = 11004;

        /// <summary>The tenant slug does not follow the slug rule (400).</summary>
        public const int TenantSlugInvalid = 11005;

        /// <summary>The tenant cannot move from its current status to the requested one (409).</summary>
        public const int TenantTransitionNotAllowed = 11006;

        /// <summary>A plan, domain or client application value is not valid (400).</summary>
        public const int CatalogValueInvalid = 11007;

        /// <summary>The endpoint needs a tenant and the request identifies none (400).</summary>
        public const int TenantRequired = 11008;

        /// <summary>No tenant with this slug or host, or the tenant is archived (404).</summary>
        public const int TenantNotFound = 11009;

        /// <summary>The tenant is being provisioned or its migration failed (503).</summary>
        public const int TenantUnavailable = 11010;

        /// <summary>The tenant is suspended (423).</summary>
        public const int TenantSuspended = 11011;

        /// <summary>A tenant was provisioned and is active.</summary>
        public const int TenantProvisioned = 11012;

        /// <summary>A tenant was suspended by the platform.</summary>
        public const int TenantWasSuspended = 11013;

        /// <summary>A suspended tenant was reactivated.</summary>
        public const int TenantWasReactivated = 11014;

        /// <summary>A tenant was archived (never deleted, D-25).</summary>
        public const int TenantWasArchived = 11015;

        /// <summary>A tenant with this slug already exists and is not being provisioned (409).</summary>
        public const int TenantAlreadyExists = 11016;

        /// <summary>The slug is reserved for the platform (400).</summary>
        public const int TenantSlugReserved = 11017;

        /// <summary>The database provided for the tenant cannot be used (500).</summary>
        public const int TenantDatabaseInvalid = 11018;

        /// <summary>The module catalog was aligned with the module descriptors of this deployment.</summary>
        public const int ModulesSynchronized = 11019;

        /// <summary>The System asked to create a tenant; provisioning runs in the Worker (N02).</summary>
        public const int TenantProvisioningRequested = 11020;

        /// <summary>The name or the time zone of a tenant changed.</summary>
        public const int TenantUpdated = 11021;

        /// <summary>A tenant moved to another plan.</summary>
        public const int TenantPlanChanged = 11022;

        /// <summary>A module override of a tenant was set or removed (D-18).</summary>
        public const int TenantModuleOverrideChanged = 11023;

        /// <summary>No active plan with this code (404).</summary>
        public const int PlanNotFound = 11024;

        /// <summary>No available module with this code (404).</summary>
        public const int ModuleNotFound = 11025;

        /// <summary>Core modules are always visible and take no override (400).</summary>
        public const int CoreModuleNotConfigurable = 11026;

        /// <summary>The provisioning message could not be sent (bus unavailable): the System retries from the console.</summary>
        public const int ProvisioningNotDispatched = 11027;

        /// <summary>The tenant is not waiting for provisioning (409).</summary>
        public const int TenantNotProvisioning = 11028;

        /// <summary>The tenant is archived and read-only (409, D-25).</summary>
        public const int TenantArchived = 11029;
    }
}
