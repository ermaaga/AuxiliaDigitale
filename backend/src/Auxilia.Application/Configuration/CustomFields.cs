using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration.Public;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.Domain.Configuration;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <summary>Custom field definitions of the current tenant (F20, D-18), managed by the System from the console.</summary>
public interface ICustomFieldManager
{
    Task<Result<Guid>> CreateAsync(CreateCustomFieldRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateCustomFieldRequest request, CancellationToken cancellationToken);

    /// <summary>Removes the definition; values already stored stay in the records but are no longer shown or accepted.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface ICustomFieldQueryService
{
    IReadOnlyList<CustomFieldEntityResponse> ListEntities();

    /// <param name="entityType">One entity (it must exist), or every entity when null.</param>
    Task<Result<IReadOnlyList<CustomFieldDefinitionResponse>>> ListAsync(string? entityType, CancellationToken cancellationToken);
}

/// <summary>The definitions of the tenant, cached with the configuration tag (<c>t:{slug}:configuration</c>).</summary>
internal sealed class CustomFieldCache(IReferenceDataCache cache, ITenantContext tenantContext, ICustomizationDataFactory data)
    : ReferenceDataCache<IReadOnlyList<CustomFieldDefinitionResponse>>(cache, tenantContext)
{
    protected override string Module => SettingsSnapshotCache.ModuleName;

    protected override string Entity => "custom-fields";

    protected override async Task<IReadOnlyList<CustomFieldDefinitionResponse>> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return [];
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.CustomFieldsAsync(null, cancellationToken)).Select(CustomFieldMapping.ToResponse).ToArray();
    }
}

internal static class CustomFieldMapping
{
    public static CustomFieldDefinitionResponse ToResponse(CustomFieldDefinition definition) => new(
        definition.Id,
        definition.EntityType,
        definition.Key,
        definition.Label,
        definition.Type.ToString(),
        [.. definition.Options],
        definition.IsRequired,
        definition.GroupName,
        definition.BadgeColor,
        definition.VisibleOnGrid,
        definition.DashboardCounter,
        definition.Order);
}

internal sealed class CustomFieldManager(
    IOperationRunner operations, ICustomizationDataFactory data, IModuleRegistry modules, ITenantContext tenantContext, IReferenceDataCache cache)
    : ICustomFieldManager
{
    public Task<Result<Guid>> CreateAsync(CreateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Configuration.CreateCustomField, new { request.EntityType, CustomFieldKey = request.Key }, async scope =>
        {
            if (string.IsNullOrEmpty(request.EntityType) || !modules.CustomFieldEntities.ContainsKey(request.EntityType))
            {
                return Errors.Configuration.CustomFieldInvalid("entityType", "validation.customFields.entityType");
            }

            if (!TryParseType(request.Type, out var type))
            {
                return Errors.Configuration.CustomFieldInvalid("type", "validation.customFields.type");
            }

            var created = CustomFieldDefinition.Create(Guid.CreateVersion7(), request.EntityType, request.Key, new CustomFieldSpec(
                request.Label, type, request.Options, request.IsRequired, request.GroupName, request.BadgeColor, request.VisibleOnGrid, request.DashboardCounter, request.Order));
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var definition = created.Value;
            if ((await store.CustomFieldsAsync(definition.EntityType, cancellationToken)).Any(item => string.Equals(item.Key, definition.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return Errors.Configuration.CustomFieldKeyTaken();
            }

            store.Add(definition);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("CustomFieldDefinition", definition.Id);
            InvalidateAfterCommit(scope);
            return Result.Success(definition.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAsync(Guid id, UpdateCustomFieldRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Configuration.UpdateCustomField, new { CustomFieldId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCustomFieldAsync(id, cancellationToken) is not { } definition)
            {
                return Errors.Configuration.CustomFieldNotFound();
            }

            var updated = definition.Update(new CustomFieldSpec(
                request.Label, definition.Type, request.Options, request.IsRequired, request.GroupName, request.BadgeColor, request.VisibleOnGrid, request.DashboardCounter, request.Order));
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Configuration.DeleteCustomField, new { CustomFieldId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindCustomFieldAsync(id, cancellationToken) is not { } definition)
            {
                return Errors.Configuration.CustomFieldNotFound();
            }

            store.Remove(definition);
            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return Result.Success();
        }, cancellationToken);

    internal static bool TryParseType(string? value, out CustomFieldType type)
    {
        type = default;
        return value is not null
            && Enum.GetNames<CustomFieldType>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out type);
    }

    private void InvalidateAfterCommit(IOperationScope scope)
    {
        var slug = tenantContext.Tenant.Slug;
        scope.OnCommitted(ct => cache.InvalidateAsync(CacheTags.Tenant(slug, SettingsSnapshotCache.ModuleName), ct));
    }
}

internal sealed class CustomFieldQueryService(IModuleRegistry modules, CustomFieldCache cache) : ICustomFieldQueryService
{
    public IReadOnlyList<CustomFieldEntityResponse> ListEntities() =>
        modules.CustomFieldEntities
            .OrderBy(entity => entity.Key, StringComparer.Ordinal)
            .Select(entity => new CustomFieldEntityResponse(entity.Key, entity.Value, new CustomFieldEntityDefinition(entity.Key).NameKey))
            .ToArray();

    public async Task<Result<IReadOnlyList<CustomFieldDefinitionResponse>>> ListAsync(string? entityType, CancellationToken cancellationToken)
    {
        if (entityType is not null && !modules.CustomFieldEntities.ContainsKey(entityType))
        {
            return Errors.Configuration.CustomFieldInvalid("entityType", "validation.customFields.entityType");
        }

        var all = await cache.GetAsync(cancellationToken);
        return entityType is null ? Result.Success(all) : all.Where(definition => definition.EntityType == entityType).ToArray();
    }
}

/// <inheritdoc cref="ICustomFieldValidator"/>
internal sealed class CustomFieldValidator(IModuleRegistry modules, CustomFieldCache cache) : ICustomFieldValidator
{
    public const int TextMaxLength = 2000;

    public async Task<Result<string>> ValidateAsync(string entityType, JsonElement? values, CancellationToken cancellationToken)
    {
        if (!modules.CustomFieldEntities.ContainsKey(entityType))
        {
            throw new ArgumentException($"Entity '{entityType}' does not carry custom fields.", nameof(entityType));
        }

        var definitions = (await cache.GetAsync(cancellationToken)).Where(definition => definition.EntityType == entityType).ToArray();
        return Validate(definitions, values);
    }

    internal static Result<string> Validate(IReadOnlyList<CustomFieldDefinitionResponse> definitions, JsonElement? values)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var normalized = new JsonObject();
        var input = values is { ValueKind: JsonValueKind.Object } element ? element : (JsonElement?)null;
        if (values is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            return Errors.Configuration.CustomFieldValuesInvalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["customFields"] = ["validation.customFields.object"] });
        }

        var byKey = definitions.ToDictionary(definition => definition.Key, StringComparer.Ordinal);
        if (input is { } provided)
        {
            foreach (var property in provided.EnumerateObject().Where(property => !byKey.ContainsKey(property.Name)))
            {
                errors[$"customFields.{property.Name}"] = ["validation.customFields.unknown"];
            }
        }

        foreach (var definition in definitions)
        {
            JsonElement value = default;
            var present = input is { } source && source.TryGetProperty(definition.Key, out value) && value.ValueKind != JsonValueKind.Null;
            if (!present)
            {
                if (definition.IsRequired)
                {
                    errors[$"customFields.{definition.Key}"] = ["validation.customFields.required"];
                }

                continue;
            }

            if (Normalize(definition, value) is { } node)
            {
                normalized[definition.Key] = node;
            }
            else if (IsEmpty(value))
            {
                if (definition.IsRequired)
                {
                    errors[$"customFields.{definition.Key}"] = ["validation.customFields.required"];
                }
            }
            else
            {
                errors[$"customFields.{definition.Key}"] = ["validation.customFields.value"];
            }
        }

        return errors.Count > 0
            ? Errors.Configuration.CustomFieldValuesInvalid(errors)
            : normalized.ToJsonString();
    }

    private static bool IsEmpty(JsonElement value) =>
        (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    /// <summary>The value in its canonical form, or null when it is empty or not valid for the type.</summary>
    private static JsonNode? Normalize(CustomFieldDefinitionResponse definition, JsonElement value)
    {
        switch (Enum.Parse<CustomFieldType>(definition.Type))
        {
            case CustomFieldType.Text when value.ValueKind == JsonValueKind.String:
                var text = value.GetString()!.Trim();
                return text.Length is > 0 and <= TextMaxLength ? JsonValue.Create(text) : null;
            case CustomFieldType.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number):
                return JsonValue.Create(number);
            case CustomFieldType.Date when value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                return JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case CustomFieldType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return JsonValue.Create(value.GetBoolean());
            case CustomFieldType.Select when value.ValueKind == JsonValueKind.String && definition.Options.Contains(value.GetString()!, StringComparer.Ordinal):
                return JsonValue.Create(value.GetString());
            case CustomFieldType.MultiSelect when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0:
                var chosen = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
                return chosen.All(item => item is not null && definition.Options.Contains(item, StringComparer.Ordinal))
                    && chosen.Distinct(StringComparer.Ordinal).Count() == chosen.Length
                        ? new JsonArray([.. definition.Options.Where(chosen.Contains).Select(option => (JsonNode?)JsonValue.Create(option))])
                        : null;
            default:
                return null;
        }
    }
}
