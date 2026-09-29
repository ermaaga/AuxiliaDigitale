using Auxilia.Application.Abstractions.Authorization;

namespace Auxilia.Application.Operations;

/// <summary>Default actor for hosts without a caller (Worker, auxctl); the Api replaces it with the authenticated user.</summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public ActorType ActorType => ActorType.System;

    public Guid? UserId => null;
}
