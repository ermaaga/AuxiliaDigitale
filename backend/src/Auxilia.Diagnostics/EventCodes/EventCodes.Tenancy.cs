namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(11000, "Tenancy / Catalog")]
    public static class Tenancy
    {
        /// <summary>The tenant in the token differs from the tenant requested by header or host (403).</summary>
        public const int CrossTenantAttempt = 11004;
    }
}
