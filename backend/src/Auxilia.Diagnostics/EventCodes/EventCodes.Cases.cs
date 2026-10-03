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

        /// <summary>A case was opened for a client (F09): number, price snapshot, client status recomputed.</summary>
        public const int CaseOpened = 14014;

        /// <summary>A case moved one status forward (Inserted → InProgress → Sent).</summary>
        public const int CaseAdvanced = 14015;

        /// <summary>A case moved one status back (InProgress or Sent).</summary>
        public const int CaseMovedBack = 14016;

        /// <summary>A case was completed with the amount received and the outcome (rejected or not).</summary>
        public const int CaseCompleted = 14017;

        /// <summary>A payment was recorded on a case.</summary>
        public const int CasePaymentRecorded = 14018;

        /// <summary>The due date or custom fields of a case changed.</summary>
        public const int CaseUpdated = 14019;

        /// <summary>A case was deleted (soft delete); the client status was recomputed.</summary>
        public const int CaseDeleted = 14020;

        /// <summary>A value of a case is not valid (400, field errors).</summary>
        public const int CaseInvalid = 14021;

        /// <summary>No case with this id visible to the caller (404, Q10).</summary>
        public const int CaseNotFound = 14022;

        /// <summary>A completed case cannot change any more (409, terminal status).</summary>
        public const int CaseIsCompleted = 14023;

        /// <summary>A sent case moves forward only by completing it with the amount and outcome (409).</summary>
        public const int CaseCompletionRequired = 14024;

        /// <summary>An inserted case cannot move back (409).</summary>
        public const int CaseCannotGoBack = 14025;

        /// <summary>A service with cases cannot be deleted: deactivate it (409, Q28).</summary>
        public const int ServiceInUse = 14026;
    }
}
