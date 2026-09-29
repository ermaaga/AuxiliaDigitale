using Auxilia.Application.Abstractions.Authorization;

namespace Auxilia.Application.Platform;

internal static class ActorDescription
{
    /// <summary><c>system</c>, <c>platform:{id}</c>, <c>user:{id}</c>.</summary>
    public static string Of(ICurrentUser user)
    {
        var actor = user.ActorType.ToString().ToLowerInvariant();
        return user.UserId is { } id ? $"{actor}:{id}" : actor;
    }
}
