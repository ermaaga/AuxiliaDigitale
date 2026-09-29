using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Paging;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auxilia.Application.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ResolvesRunnerDefaultsAndValidators()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOperationRunner>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().ActorType.ShouldBe(ActorType.System);
        scope.ServiceProvider.GetRequiredService<IValidator<PageRequest>>().ShouldBeOfType<PageRequestValidator>();
    }

    [Fact]
    public void AddApplication_KeepsHostCurrentUser()
    {
        var services = new ServiceCollection();
        var hostUser = NSubstitute.Substitute.For<ICurrentUser>();
        services.AddScoped(_ => hostUser);

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().ShouldBeSameAs(hostUser);
    }
}
