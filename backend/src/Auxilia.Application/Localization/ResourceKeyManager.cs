using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Localization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Localization;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Localization;

/// <summary>
/// The resource editor of a tenant (F24, System console, D-18): keys and translations. Every edit marks the translation
/// as customised (data-migrations never overwrite it) and evicts the tenant's languages and bundles after the commit,
/// so clients see it at their next request (new ETag), without restarts or reloads.
/// </summary>
public interface IResourceKeyManager
{
    Task<Result<Guid>> CreateAsync(CreateResourceKey request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(UpdateResourceKey request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the translation of an existing language.</summary>
    Task<Result> SetTranslationAsync(Guid id, string languageCode, string value, CancellationToken cancellationToken);

    /// <summary>Removes a translation (404 <c>AUX-21011</c> when the key has none in that language).</summary>
    Task<Result> RemoveTranslationAsync(Guid id, string languageCode, CancellationToken cancellationToken);
}

/// <param name="Translations">Language code → value; languages must exist.</param>
public sealed record CreateResourceKey(string Key, string Category, string? Description, IReadOnlyDictionary<string, string>? Translations);

public sealed record UpdateResourceKey(Guid Id, string Category, string? Description);

internal sealed class ResourceKeyManager : IResourceKeyManager
{
    private readonly IOperationRunner operations;
    private readonly ILocalizationDataFactory data;
    private readonly ITenantContext tenantContext;
    private readonly IReferenceDataCache cache;

    public ResourceKeyManager(IOperationRunner operations, ILocalizationDataFactory data, ITenantContext tenantContext, IReferenceDataCache cache)
    {
        this.operations = operations;
        this.data = data;
        this.tenantContext = tenantContext;
        this.cache = cache;
    }

    public Task<Result<Guid>> CreateAsync(CreateResourceKey request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Key?.Trim() ?? string.Empty;
        return operations.RunAsync(Operations.Localization.CreateKey, new { ResourceKey = name }, async scope =>
        {
            var created = ResourceKey.Create(Guid.CreateVersion7(), name, request.Category?.Trim() ?? string.Empty, request.Description, isSystem: false);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var key = created.Value;
            var languages = await store.LanguagesAsync(cancellationToken);
            foreach (var (language, value) in request.Translations ?? new Dictionary<string, string>())
            {
                if (!languages.Any(item => item.Code == language))
                {
                    return Errors.Localization.LanguageNotFound(language);
                }

                var set = key.SetTranslation(language, value);
                if (set.IsFailure)
                {
                    return Result.Failure<Guid>(set.Error!);
                }
            }

            if (await store.KeyExistsAsync(key.Key, cancellationToken))
            {
                return Errors.Localization.ResourceKeyExists(key.Key);
            }

            store.Add(key);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("ResourceKey", key.Id);
            InvalidateAfterCommit(scope);
            return Result.Success(key.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(UpdateResourceKey request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WithKeyAsync(Operations.Localization.UpdateKey, request.Id, (key, _) =>
            Task.FromResult(key.Update(request.Category?.Trim() ?? string.Empty, request.Description)), cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        WithKeyAsync(Operations.Localization.DeleteKey, id, (key, store) =>
        {
            store.Remove(key);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result> SetTranslationAsync(Guid id, string languageCode, string value, CancellationToken cancellationToken) =>
        WithKeyAsync(Operations.Localization.SetTranslation, id, async (key, store) =>
        {
            if (!(await store.LanguagesAsync(cancellationToken)).Any(language => language.Code == languageCode))
            {
                return Errors.Localization.LanguageNotFound(languageCode);
            }

            return key.SetTranslation(languageCode, value);
        }, cancellationToken);

    public Task<Result> RemoveTranslationAsync(Guid id, string languageCode, CancellationToken cancellationToken) =>
        WithKeyAsync(Operations.Localization.RemoveTranslation, id, (key, _) =>
            Task.FromResult(key.RemoveTranslation(languageCode) ? Result.Success() : Result.Failure(Errors.Localization.TranslationNotFound(languageCode))),
            cancellationToken);

    private Task<Result> WithKeyAsync(
        Diagnostics.OperationDescriptor operation, Guid id, Func<ResourceKey, ILocalizationData, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { ResourceKeyId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindKeyAsync(id, cancellationToken) is not { } key)
            {
                return Errors.Localization.ResourceKeyNotFound();
            }

            scope.SetEntity("ResourceKey", key.Id);
            var result = await change(key, store);
            if (result.IsFailure)
            {
                return result;
            }

            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return Result.Success();
        }, cancellationToken);

    /// <summary>Languages and every bundle of the tenant share the tag <c>t:{slug}:localization</c>.</summary>
    private void InvalidateAfterCommit(IOperationScope scope)
    {
        var tag = CacheTags.Tenant(tenantContext.Tenant.Slug, LocalizationModule.ModuleCode);
        scope.OnCommitted(ct => cache.InvalidateAsync(tag, ct));
    }
}
