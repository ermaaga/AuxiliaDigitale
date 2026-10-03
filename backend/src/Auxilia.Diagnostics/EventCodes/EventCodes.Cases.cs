namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(14000, "Cases (services, cases, payments)")]
    public static class Cases
    {
        /// <summary>A service category was created (F08, Q26).</summary>
        public const int ServiceCategoryCreated = 14001;

        /// <summary>A service category was changed or (de)activated.</summary>
        public const int ServiceCategoryUpdated = 14002;

        /// <summary>A service category nobody uses was deleted.</summary>
        public const int ServiceCategoryDeleted = 14003;

        /// <summary>A value of a service category is not valid (400, field errors).</summary>
        public const int ServiceCategoryInvalid = 14004;

        /// <summary>No service category with this id (404).</summary>
        public const int ServiceCategoryNotFound = 14005;

        /// <summary>Another active category has this name (409).</summary>
        public const int ServiceCategoryNameTaken = 14006;

        /// <summary>A category used by a service cannot be deleted: deactivate it (409).</summary>
        public const int ServiceCategoryInUse = 14007;

        /// <summary>A service of the catalog was created (F08).</summary>
        public const int ServiceCreated = 14008;

        /// <summary>A service was changed or (de)activated (Q27: the specialization is kept).</summary>
        public const int ServiceUpdated = 14009;

        /// <summary>A service was deleted (soft delete, Q28).</summary>
        public const int ServiceDeleted = 14010;

        /// <summary>A value of a service is not valid (400, field errors).</summary>
        public const int ServiceInvalid = 14011;

        /// <summary>No service with this id (404).</summary>
        public const int ServiceNotFound = 14012;

        /// <summary>Another service has this name (409).</summary>
        public const int ServiceNameTaken = 14013;
    }
}
