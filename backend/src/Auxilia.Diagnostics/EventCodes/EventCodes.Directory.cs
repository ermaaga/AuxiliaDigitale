namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(13000, "Directory (clients, employees)")]
    public static class Directory
    {
        /// <summary>A role specialization was created (F12).</summary>
        public const int SpecializationCreated = 13001;

        /// <summary>A role specialization was changed.</summary>
        public const int SpecializationUpdated = 13002;

        /// <summary>A role specialization was deactivated (it keeps its members and disappears from the lists).</summary>
        public const int SpecializationDeactivated = 13003;

        /// <summary>A role specialization is not valid (name, role, e-mail…) (400).</summary>
        public const int SpecializationInvalid = 13004;

        /// <summary>No active specialization with this id (404).</summary>
        public const int SpecializationNotFound = 13005;

        /// <summary>The role already has an active specialization with this name (409).</summary>
        public const int SpecializationNameTaken = 13006;

        /// <summary>Users were added to a specialization.</summary>
        public const int SpecializationMembersAdded = 13007;

        /// <summary>A user was removed from a specialization.</summary>
        public const int SpecializationMemberRemoved = 13008;

        /// <summary>A user to add does not exist or does not have the specialization's role (400).</summary>
        public const int SpecializationMemberInvalid = 13009;
    }
}
