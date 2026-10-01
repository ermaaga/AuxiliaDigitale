using System.Text.Json;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Configuration;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <summary>
/// The settings of the current tenant as the System edits them (F23, technical endpoints): every definition that allows
/// the tenant level, with code default, platform and tenant values and the effective one. Secret values never leave.
/// </summary>
public interface ISettingsQueryService
{
    Task<Result<IReadOnlyList<SettingResponse>>> ListTenantSettingsAsync(CancellationToken cancellationToken);

    Task<Result<SettingResponse>> GetTenantSettingAsync(string key, CancellationToken cancellationToken);
}

internal sealed class SettingsQueryService(ISettingDefinitionRegistry definitions, SettingsSnapshotCache snapshots, ITenantContext tenantContext)
    : ISettingsQueryService
{
    public async Task<Result<IReadOnlyList<SettingResponse>>> ListTenantSettingsAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.IsResolved)
        {
            return Errors.Tenancy.TenantRequired();
        }

        var snapshot = await snapshots.GetAsync(cancellationToken);
        return definitions.All
            .Where(definition => definition.Allows(SettingScope.Tenant))
            .OrderBy(definition => definition.Module, StringComparer.Ordinal)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(definition => View(definition, snapshot))
            .ToArray();
    }

    public async Task<Result<SettingResponse>> GetTenantSettingAsync(string key, CancellationToken cancellationToken)
    {
        if (!tenantContext.IsResolved)
        {
            return Errors.Tenancy.TenantRequired();
        }

        if (definitions.Find(key) is not { } definition || !definition.Allows(SettingScope.Tenant))
        {
            return Errors.Configuration.SettingNotFound(key);
        }

        return View(definition, await snapshots.GetAsync(cancellationToken));
    }

    /// <summary>
    /// The levels of one setting. A stored value that the definition no longer accepts is shown as stored but does not
    /// count (the provider skips it the same way).
    /// </summary>
    internal static SettingResponse View(SettingDefinition definition, SettingsSnapshot snapshot)
    {
        var platform = Valid(definition, snapshot.Platform.GetValueOrDefault(definition.Key));
        var tenant = Valid(definition, snapshot.Tenant.GetValueOrDefault(definition.Key));
        var hasTenantValue = snapshot.Tenant.ContainsKey(definition.Key);

        if (definition.IsSecret)
        {
            var secretSource = tenant is not null ? SettingSource.Tenant : platform is not null ? SettingSource.Platform : SettingSource.Default;
            return new SettingResponse(definition.Key, definition.Module, "secret", null, null, null, null, null, secretSource.ToString(), hasTenantValue);
        }

        var (effective, source) = tenant is not null && definition.Allows(SettingScope.Tenant)
            ? (tenant, SettingSource.Tenant)
            : platform is not null && definition.Allows(SettingScope.Platform)
                ? (platform, SettingSource.Platform)
                : (definition.DefaultJson, SettingSource.Default);

        return new SettingResponse(
            definition.Key,
            definition.Module,
            KindOf(definition),
            definition.Choices,
            Parse(definition.DefaultJson),
            Parse(platform),
            Parse(tenant),
            Parse(effective),
            source.ToString(),
            hasTenantValue);
    }

    internal static string KindOf(SettingDefinition definition) => definition switch
    {
        { IsSecret: true } => "secret",
        { Choices: not null } => "choice",
        _ when definition.ValueType == typeof(bool) => "boolean",
        _ when definition.ValueType == typeof(int) || definition.ValueType == typeof(long) => "integer",
        _ when definition.ValueType == typeof(decimal) || definition.ValueType == typeof(double) => "number",
        _ => "string",
    };

    private static string? Valid(SettingDefinition definition, string? json)
    {
        if (json is null)
        {
            return null;
        }

        if (definition.IsSecret)
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);
        return definition.TryNormalize(document.RootElement, out var normalized) ? normalized : null;
    }

    private static JsonElement? Parse(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
