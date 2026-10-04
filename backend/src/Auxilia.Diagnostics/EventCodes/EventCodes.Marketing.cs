namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(19000, "Marketing")]
    public static class Marketing
    {
        /// <summary>A dynamic segment was created (N01, M-02).</summary>
        public const int SegmentCreated = 19001;

        /// <summary>A segment was renamed or its rule changed.</summary>
        public const int SegmentUpdated = 19002;

        /// <summary>A segment was deleted.</summary>
        public const int SegmentDeleted = 19003;

        /// <summary>A segment or its rule is not valid (400, field errors <c>rule.conditions[i].value</c>…).</summary>
        public const int SegmentInvalid = 19004;

        /// <summary>No segment with this id (404).</summary>
        public const int SegmentNotFound = 19005;

        /// <summary>A static list was created.</summary>
        public const int ListCreated = 19006;

        /// <summary>A static list was renamed.</summary>
        public const int ListUpdated = 19007;

        /// <summary>A static list was deleted with its members.</summary>
        public const int ListDeleted = 19008;

        /// <summary>A static list or its members are not valid (400, field errors).</summary>
        public const int ListInvalid = 19009;

        /// <summary>No static list with this id (404).</summary>
        public const int ListNotFound = 19010;

        /// <summary>Clients were added to or removed from a static list.</summary>
        public const int ListMembersChanged = 19011;
    }
}
