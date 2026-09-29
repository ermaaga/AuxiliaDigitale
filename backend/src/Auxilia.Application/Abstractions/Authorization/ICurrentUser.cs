namespace Auxilia.Application.Abstractions.Authorization;

/// <summary>Who runs the current operation. Permissions and tenant come with Identity (P2) and Tenancy (P1-07).</summary>
public interface ICurrentUser
{
    ActorType ActorType { get; }

    /// <summary>Tenant user or platform user id; null for anonymous callers and the system.</summary>
    Guid? UserId { get; }

    bool IsAuthenticated => ActorType is ActorType.User or ActorType.Platform;
}

/// <summary>Kind of actor, also recorded by the audit (<c>actor_type</c>).</summary>
public enum ActorType
{
    Anonymous,

    /// <summary>A tenant user (Administrator, Employee, Client).</summary>
    User,

    /// <summary>A platform user (System role, console).</summary>
    Platform,

    /// <summary>The application itself: Worker handlers, auxctl, manual job runs.</summary>
    System,
}
