using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <inheritdoc cref="ISettingsManager"/>
internal sealed class SettingsManager : ISettingsManager
{
    private readonly IOperationRunner operations;
    private readonly ISettingDefinitionRegistry definitions;
    private readonly IPlatformSettingStore platformSettings;
    private readonly ITenantSettingStore tenantSettings;
    private readonly ITenantContext tenantContext;
    private readonly ICurrentUser currentUser;
    private readonly ISettingSecretProtector secrets;
    private readonly SettingsSnapshotCache snapshots;

    public SettingsManager(
        IOperationRunner operations,
        ISettingDefinitionRegistry definitions,
        IPlatformSettingStore platformSettings,
        ITenantSettingStore tenantSettings,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ISettingSecretProtector secrets,
        SettingsSnapshotCache snapshots)
    {
        this.operations = operations;
        this.definitions = definitions;
        this.platformSettings = platformSettings;
        this.tenantSettings = tenantSettings;
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.secrets = secrets;
        this.snapshots = snapshots;
    }

    public Task<Result> SetAsync(SetSetting request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Configuration.SetSetting, Context(request.Key, request.Level), async scope =>
        {
            var target = Resolve(request.Key, request.Level);
            if (target.IsFailure)
            {
                return target;
            }

            var definition = target.Value;
            if (!definition.TryNormalize(request.Value, out var json))
            {
                return Errors.Configuration.SettingValueInvalid(definition.Key);
            }

            if (definition.IsSecret)
            {
                json = JsonSerializer.Serialize(secrets.Protect(JsonSerializer.Deserialize<string>(json)!));
            }

            switch (request.Level)
            {
                case SettingScope.Platform:
                    await platformSettings.SetAsync(definition.Key, json, cancellationToken);
                    break;
                case SettingScope.Tenant:
                    await tenantSettings.SetTenantValueAsync(definition.Key, json, cancellationToken);
                    break;
                default:
                    await tenantSettings.SetUserValueAsync(CurrentUserId()!.Value, definition.Key, json, cancellationToken);
                    break;
            }

            InvalidateAfterCommit(scope, request.Level);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> ResetAsync(ResetSetting request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Configuration.ResetSetting, Context(request.Key, request.Level), async scope =>
        {
            var target = Resolve(request.Key, request.Level);
            if (target.IsFailure)
            {
                return target;
            }

            switch (request.Level)
            {
                case SettingScope.Platform:
                    await platformSettings.RemoveAsync(request.Key, cancellationToken);
                    break;
                case SettingScope.Tenant:
                    await tenantSettings.RemoveTenantValueAsync(request.Key, cancellationToken);
                    break;
                default:
                    await tenantSettings.RemoveUserValueAsync(CurrentUserId()!.Value, request.Key, cancellationToken);
                    break;
            }

            InvalidateAfterCommit(scope, request.Level);
            return Result.Success();
        }, cancellationToken);
    }

    private static object Context(string key, SettingScope level) => new { SettingKey = key, SettingLevel = level.ToString() };

    /// <summary>The definition, if it exists and allows the level, and the level has what it needs (tenant, user).</summary>
    private Result<SettingDefinition> Resolve(string key, SettingScope level)
    {
        if (definitions.Find(key) is not { } definition)
        {
            return Errors.Configuration.SettingNotFound(key);
        }

        if (level is not (SettingScope.Platform or SettingScope.Tenant or SettingScope.User) || !definition.Allows(level))
        {
            return Errors.Configuration.SettingScopeNotAllowed(key, level.ToString());
        }

        if (level is SettingScope.Tenant or SettingScope.User && !tenantContext.IsResolved)
        {
            return Errors.Tenancy.TenantRequired();
        }

        if (level == SettingScope.User && CurrentUserId() is null)
        {
            return Errors.Configuration.SettingScopeNotAllowed(key, level.ToString());
        }

        return definition;
    }

    /// <summary>User values are not cached; platform values are part of every tenant snapshot.</summary>
    private void InvalidateAfterCommit(IOperationScope scope, SettingScope level)
    {
        if (level == SettingScope.Platform)
        {
            scope.OnCommitted(snapshots.InvalidatePlatformAsync);
        }
        else if (level == SettingScope.Tenant)
        {
            var slug = tenantContext.Tenant.Slug;
            scope.OnCommitted(ct => snapshots.InvalidateTenantAsync(slug, ct));
        }
    }

    private Guid? CurrentUserId() => currentUser.ActorType == ActorType.User ? currentUser.UserId : null;
}
