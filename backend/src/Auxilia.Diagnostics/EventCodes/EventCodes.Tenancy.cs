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
    }
}
