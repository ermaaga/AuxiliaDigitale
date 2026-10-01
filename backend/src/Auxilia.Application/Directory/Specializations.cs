using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Directory;
using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Directory;

/// <summary>Role specializations of the current tenant (F12, D-18), managed by the System from the console.</summary>
public interface ISpecializationManager
{
    Task<Result<Guid>> CreateAsync(CreateSpecializationRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateSpecializationRequest request, CancellationToken cancellationToken);

    /// <summary>Legacy delete: the specialization is deactivated and keeps its members.</summary>
    Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds users of the specialization's role (users already holding it are ignored).</summary>
    Task<Result> AddMembersAsync(Guid id, IReadOnlyList<Guid>? userIds, CancellationToken cancellationToken);

    /// <summary>Idempotent: removing a user who does not hold the specialization succeeds.</summary>
    Task<Result> RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}

public interface ISpecializationQueryService
{
    /// <param name="role"><c>Client</c>, <c>Employee</c> or null for both.</param>
    Task<Result<IReadOnlyList<SpecializationResponse>>> ListAsync(string? role, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SpecializationMemberResponse>>> MembersAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Users of the role that can be added, at most <see cref="SpecializationQueryService.CandidateLimit"/>.</summary>
    Task<Result<IReadOnlyList<SpecializationMemberResponse>>> CandidatesAsync(Guid id, string? search, CancellationToken cancellationToken);
}

internal static class SpecializationMapping
{
    public static SpecializationResponse ToResponse(Specialization specialization) => new(
        specialization.Id,
        specialization.Name,
        specialization.Role.ToString(),
        specialization.Description,
        specialization.Email,
        specialization.WorkPhone,
        specialization.IsPrivate,
        specialization.Members.Count);

    public static SpecializationMemberResponse ToResponse(DirectoryUser user) =>
        new(user.UserId, user.UserName, user.FullName, user.Email, user.IsActive);

    public static bool TryParseRole(string? value, out TenantRole role) =>
        TenantRoles.TryParse(value, out role) && Specialization.Roles.Contains(role);
}

internal sealed class SpecializationManager(IOperationRunner operations, ISpecializationDataFactory data, TimeProvider clock) : ISpecializationManager
{
    /// <summary>Users added in one request.</summary>
    public const int MaxMembersPerRequest = 100;

    public Task<Result<Guid>> CreateAsync(CreateSpecializationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Directory.CreateSpecialization, new { request.Role }, async scope =>
        {
            if (!SpecializationMapping.TryParseRole(request.Role, out var role))
            {
                return Errors.Directory.SpecializationInvalid("role", "validation.specializations.role");
            }

            var created = Specialization.Create(Guid.CreateVersion7(), role, Spec(request.Name, request.Description, request.Email, request.WorkPhone, request.IsPrivate));
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            var specialization = created.Value;
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.NameTakenAsync(role, specialization.Name, null, cancellationToken))
            {
                return Errors.Directory.SpecializationNameTaken();
            }

            store.Add(specialization);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Specialization), specialization.Id);
            return Result.Success(specialization.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateSpecializationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Directory.UpdateSpecialization, new { SpecializationId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } specialization)
            {
                return Errors.Directory.SpecializationNotFound();
            }

            // Checked before changing the tracked entity: a refused update leaves it untouched.
            if (request.Name?.Trim() is { Length: > 0 } name && await store.NameTakenAsync(specialization.Role, name, id, cancellationToken))
            {
                return Errors.Directory.SpecializationNameTaken();
            }

            var updated = specialization.Update(Spec(request.Name, request.Description, request.Email, request.WorkPhone, request.IsPrivate));
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.DeactivateSpecialization, new { SpecializationId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } specialization)
            {
                return Errors.Directory.SpecializationNotFound();
            }

            specialization.Deactivate();
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> AddMembersAsync(Guid id, IReadOnlyList<Guid>? userIds, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.AddSpecializationMembers, new { SpecializationId = id, Count = userIds?.Count ?? 0 }, async _ =>
        {
            var requested = (userIds ?? []).Distinct().ToArray();
            if (requested.Length is 0 or > MaxMembersPerRequest)
            {
                return Errors.Directory.SpecializationInvalid("userIds", "validation.specializations.members");
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } specialization)
            {
                return Errors.Directory.SpecializationNotFound();
            }

            // Q30/Q34: only users of the specialization's role, never another role.
            if ((await store.UsersWithRoleAsync(requested, specialization.Role, cancellationToken)).Count != requested.Length)
            {
                return Errors.Directory.SpecializationMemberInvalid();
            }

            if (specialization.AddMembers(requested, clock.GetUtcNow()) > 0)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result> RemoveMemberAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Directory.RemoveSpecializationMember, new { SpecializationId = id, UserId = userId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } specialization)
            {
                return Errors.Directory.SpecializationNotFound();
            }

            if (specialization.RemoveMember(userId))
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    private static SpecializationSpec Spec(string? name, string? description, string? email, string? workPhone, bool isPrivate) =>
        new(name ?? string.Empty, description, email, workPhone, isPrivate);
}

internal sealed class SpecializationQueryService(ISpecializationDataFactory data) : ISpecializationQueryService
{
    public const int CandidateLimit = 50;
    public const int SearchMaxLength = 100;

    public async Task<Result<IReadOnlyList<SpecializationResponse>>> ListAsync(string? role, CancellationToken cancellationToken)
    {
        TenantRole? filter = null;
        if (!string.IsNullOrEmpty(role))
        {
            if (!SpecializationMapping.TryParseRole(role, out var parsed))
            {
                return Errors.Directory.SpecializationInvalid("role", "validation.specializations.role");
            }

            filter = parsed;
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.ListAsync(filter, cancellationToken)).Select(SpecializationMapping.ToResponse).ToArray();
    }

    public async Task<Result<IReadOnlyList<SpecializationMemberResponse>>> MembersAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, cancellationToken) is null)
        {
            return Errors.Directory.SpecializationNotFound();
        }

        return (await store.MembersAsync(id, cancellationToken)).Select(SpecializationMapping.ToResponse).ToArray();
    }

    public async Task<Result<IReadOnlyList<SpecializationMemberResponse>>> CandidatesAsync(Guid id, string? search, CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term is { Length: > SearchMaxLength })
        {
            return Errors.Directory.SpecializationInvalid("search", "validation.specializations.search");
        }

        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, cancellationToken) is not { } specialization)
        {
            return Errors.Directory.SpecializationNotFound();
        }

        return (await store.CandidatesAsync(id, specialization.Role, term, CandidateLimit, cancellationToken))
            .Select(SpecializationMapping.ToResponse)
            .ToArray();
    }
}
