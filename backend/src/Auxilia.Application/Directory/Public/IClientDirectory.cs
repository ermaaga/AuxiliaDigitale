using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Identity.Public;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Directory.Public;

/// <summary>A client as other modules see it.</summary>
public sealed record ClientSummary(Guid Id, string FullName, Guid? EmployeeUserId);

/// <summary>
/// The clients of the current tenant for the modules that work on them (Cases, B-08). Writes join the caller's
/// running write operation (same transaction).
/// </summary>
public interface IClientDirectory
{
    /// <summary>The client, <c>null</c> when it does not exist or is deleted.</summary>
    Task<ClientSummary?> FindAsync(Guid clientId, CancellationToken cancellationToken);

    /// <summary>
    /// Hands the client to <paramref name="employeeUserId"/> when given (an active user with the Employee role), else to
    /// the default employee (Q31) when nobody is in charge. Returns who is in charge afterwards.
    /// </summary>
    Task<Result<Guid?>> EnsureEmployeeAsync(Guid clientId, Guid? employeeUserId, CancellationToken cancellationToken);

    /// <summary>Q03: the business status follows the open cases (D-05: sign-in does not change).</summary>
    Task<Result> UpdateStatusAsync(Guid clientId, bool hasOpenCases, CancellationToken cancellationToken);
}

internal sealed class ClientDirectory(IClientDataFactory data, IUserAccounts accounts, TimeProvider clock) : IClientDirectory
{
    public async Task<ClientSummary?> FindAsync(Guid clientId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindPersonAsync(clientId, cancellationToken) is { } person
            && await store.FindProfileAsync(clientId, cancellationToken) is { } profile
            ? new ClientSummary(person.Id, person.FullName, profile.EmployeeUserId)
            : null;
    }

    public async Task<Result<Guid?>> EnsureEmployeeAsync(Guid clientId, Guid? employeeUserId, CancellationToken cancellationToken)
    {
        if (employeeUserId is { } wanted
            && !(await accounts.FindManyAsync([wanted], cancellationToken)).Any(account => account.CanSignIn && account.Roles.Contains(TenantRole.Employee)))
        {
            return Errors.Directory.EmployeeInvalid();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindProfileAsync(clientId, cancellationToken) is not { } profile)
        {
            return Errors.Directory.ClientNotFound();
        }

        var employee = employeeUserId ?? (profile.EmployeeUserId is null ? await store.DefaultEmployeeAsync(cancellationToken) : null);
        if (employee is { } assigned && profile.Assign(assigned, clock.GetUtcNow()))
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return profile.EmployeeUserId;
    }

    public async Task<Result> UpdateStatusAsync(Guid clientId, bool hasOpenCases, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindProfileAsync(clientId, cancellationToken) is not { } profile)
        {
            return Errors.Directory.ClientNotFound();
        }

        if (profile.UpdateStatus(hasOpenCases, clock.GetUtcNow()))
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
