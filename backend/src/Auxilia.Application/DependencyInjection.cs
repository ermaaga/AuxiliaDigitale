using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Operations;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Application base (D-26): operation runner, validators of this assembly and defaults that hosts or
    /// persistence replace (<see cref="ICurrentUser"/>, <see cref="IOperationTransactionFactory"/>).
    /// Module services are added by their module descriptors (task P1-11).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.TryAddScoped<IOperationTransactionFactory, NoOperationTransactionFactory>();
        services.TryAddScoped<IOperationRunner, OperationRunner>();

        services.AddValidatorsFrom(typeof(DependencyInjection).Assembly);

        return services;
    }

    /// <summary>Registers every concrete <see cref="IValidator{T}"/> of the assembly as a singleton (validators are stateless).</summary>
    public static IServiceCollection AddValidatorsFrom(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var validators = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(contract => (Contract: contract, Implementation: type)));

        foreach (var (contract, implementation) in validators)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(contract, implementation));
        }

        return services;
    }
}
