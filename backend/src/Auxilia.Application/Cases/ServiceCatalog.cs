using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>
/// The service catalog of the current tenant (F08, Q26–Q28): categories and services, managed by Administrators. A
/// category or service in use is deactivated rather than deleted.
/// </summary>
public interface IServiceCatalogManager
{
    Task<Result<Guid>> CreateCategoryAsync(CreateServiceCategoryRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateCategoryAsync(Guid id, UpdateServiceCategoryRequest request, CancellationToken cancellationToken);

    /// <summary>Only a category no service uses (deleted services included); otherwise <c>AUX-14007</c>.</summary>
    Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateServiceAsync(CreateServiceRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateServiceAsync(Guid id, UpdateServiceRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete (Q28): hidden everywhere. Services with cases will be refused (B-08); deactivate them instead.</summary>
    Task<Result> DeleteServiceAsync(Guid id, CancellationToken cancellationToken);
}

public interface IServiceCatalogQueryService
{
    Task<IReadOnlyList<ServiceCategoryResponse>> CategoriesAsync(CancellationToken cancellationToken);

    Task<Result<PagedResponse<ServiceResponse>>> ListAsync(ServiceListQuery query, CancellationToken cancellationToken);

    Task<Result<ServiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class ServiceCatalogManager(IOperationRunner operations, IServiceCatalogDataFactory data) : IServiceCatalogManager
{
    public Task<Result<Guid>> CreateCategoryAsync(CreateServiceCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.CreateServiceCategory, null, async scope =>
        {
            var created = ServiceCategory.Create(Guid.CreateVersion7(), request.Name, request.Description);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            var category = created.Value;
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.CategoryNameTakenAsync(category.Name, null, cancellationToken))
            {
                return Errors.Cases.ServiceCategoryNameTaken();
            }

            store.Add(category);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(ServiceCategory), category.Id);
            return Result.Success(category.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateCategoryAsync(Guid id, UpdateServiceCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.UpdateServiceCategory, new { ServiceCategoryId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCategoryAsync(id, cancellationToken) is not { } category)
            {
                return Errors.Cases.ServiceCategoryNotFound();
            }

            // Checked before changing the tracked entity: a refused update leaves it untouched.
            if (request.IsActive && request.Name?.Trim() is { Length: > 0 } name && await store.CategoryNameTakenAsync(name, id, cancellationToken))
            {
                return Errors.Cases.ServiceCategoryNameTaken();
            }

            var updated = category.Update(request.Name, request.Description, request.IsActive);
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.DeleteServiceCategory, new { ServiceCategoryId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCategoryAsync(id, cancellationToken) is not { } category)
            {
                return Errors.Cases.ServiceCategoryNotFound();
            }

            if (await store.CategoryInUseAsync(id, cancellationToken))
            {
                return Errors.Cases.ServiceCategoryInUse();
            }

            store.Remove(category);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<Guid>> CreateServiceAsync(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.CreateService, null, async scope =>
        {
            var details = new ServiceDetails(
                request.Name, request.Description, request.Price, request.DurationDays, request.CategoryId, request.SpecializationId, IsActive: true);
            var created = Service.Create(Guid.CreateVersion7(), details);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            var service = created.Value;
            await using var store = await data.OpenAsync(cancellationToken);
            var references = await CheckReferencesAsync(store, details, null, cancellationToken);
            if (references.IsFailure)
            {
                return Result.Failure<Guid>(references.Error!);
            }

            if (await store.NameTakenAsync(service.Name, null, cancellationToken))
            {
                return Errors.Cases.ServiceNameTaken();
            }

            store.Add(service);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(Service), service.Id);
            return Result.Success(service.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateServiceAsync(Guid id, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Cases.UpdateService, new { ServiceId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } service)
            {
                return Errors.Cases.ServiceNotFound();
            }

            var details = new ServiceDetails(
                request.Name, request.Description, request.Price, request.DurationDays, request.CategoryId, request.SpecializationId, request.IsActive);
            var references = await CheckReferencesAsync(store, details, service, cancellationToken);
            if (references.IsFailure)
            {
                return references;
            }

            if (request.Name?.Trim() is { Length: > 0 } name && await store.NameTakenAsync(name, id, cancellationToken))
            {
                return Errors.Cases.ServiceNameTaken();
            }

            // Q27: the specialization of the request is always applied, on create as on update.
            var updated = service.Update(details);
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteServiceAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Cases.DeleteService, new { ServiceId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { } service)
            {
                return Errors.Cases.ServiceNotFound();
            }

            store.Remove(service);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>
    /// A new category or specialization must be active (the specialization of the Employee role); the ones a service
    /// already has may stay after they were deactivated.
    /// </summary>
    private static async Task<Result> CheckReferencesAsync(IServiceCatalogData store, ServiceDetails details, Service? current, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (details.CategoryId is { } categoryId && categoryId != current?.CategoryId
            && !await store.IsActiveCategoryAsync(categoryId, cancellationToken))
        {
            errors["categoryId"] = ["validation.services.category"];
        }

        if (details.SpecializationId is { } specializationId && specializationId != current?.SpecializationId
            && !await store.IsActiveEmployeeSpecializationAsync(specializationId, cancellationToken))
        {
            errors["specializationId"] = ["validation.services.specialization"];
        }

        return errors.Count > 0 ? Errors.Cases.ServiceInvalid(errors) : Result.Success();
    }
}

internal sealed class ServiceCatalogQueryService(IServiceCatalogDataFactory data) : IServiceCatalogQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;

    private static readonly Dictionary<string, ServiceSort> Sorts = new(StringComparer.Ordinal)
    {
        ["name"] = ServiceSort.Name,
        ["price"] = ServiceSort.Price,
        ["durationDays"] = ServiceSort.DurationDays,
    };

    public async Task<IReadOnlyList<ServiceCategoryResponse>> CategoriesAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.CategoriesAsync(cancellationToken))
            .Select(row => new ServiceCategoryResponse(row.Category.Id, row.Category.Name, row.Category.Description, row.Category.IsActive, row.ServiceCount))
            .ToArray();
    }

    public async Task<Result<PagedResponse<ServiceResponse>>> ListAsync(ServiceListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = ServiceSort.Name;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        if (query.Name is { Length: > MaxFilterLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new ServiceFilter(
                string.IsNullOrWhiteSpace(query.Name) ? null : query.Name.Trim(),
                query.CategoryId,
                query.SpecializationId,
                query.Active,
                sort,
                query.Sort?.StartsWith('-') == true,
                (query.Page - 1) * query.PageSize,
                query.PageSize),
            cancellationToken);
        return new PagedResponse<ServiceResponse>(items.Select(ToResponse).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<ServiceResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.GetAsync(id, cancellationToken) is { } row ? ToResponse(row) : Errors.Cases.ServiceNotFound();
    }

    private static ServiceResponse ToResponse(ServiceRow row)
    {
        var service = row.Service;
        return new ServiceResponse(
            service.Id,
            service.Name,
            service.Description,
            service.Price,
            service.Currency,
            service.DurationDays,
            service.IsActive,
            service.CategoryId is { } categoryId ? new ServiceCategoryRefResponse(categoryId, row.CategoryName ?? string.Empty, row.CategoryActive) : null,
            service.SpecializationId is { } specializationId
                ? new ServiceSpecializationResponse(specializationId, row.SpecializationName ?? string.Empty, row.SpecializationPrivate)
                : null);
    }
}
