using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Execution;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Platform;

/// <summary>A real <see cref="OperationRunner"/> without transactions, and a system caller.</summary>
internal static class ManagerHarness
{
    public static ICurrentUser System()
    {
        var user = Substitute.For<ICurrentUser>();
        user.ActorType.Returns(ActorType.System);
        return user;
    }

    public static OperationRunner Runner() =>
        new(NullLogger<OperationRunner>.Instance, System(), new NoOperationTransactionFactory(), [], TimeProvider.System);
}
