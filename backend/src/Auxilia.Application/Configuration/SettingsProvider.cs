using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Configuration;

/// <inheritdoc cref="ISettingsProvider"/>
internal sealed class SettingsProvider : ISettingsProvider
{
    private readonly SettingsSnapshotCache snapshots;
    private readonly ITenantContext tenantContext;
    private readonly ICurrentUser currentUser;
    private readonly ITenantSettingStore tenantSettings;
    private readonly ISettingSecretProtector secrets;
    private readonly ILogger<SettingsProvider> logger;

    public SettingsProvider(
        SettingsSnapshotCache snapshots,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ITenantSettingStore tenantSettings,
        ISettingSecretProtector secrets,
        ILogger<SettingsProvider> logger)
    {
        this.snapshots = snapshots;
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.tenantSettings = tenantSettings;
        this.secrets = secrets;
        this.logger = logger;
    }

    public async Task<T> GetAsync<T>(SettingDefinition<T> definition, CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(definition);

        // User values are read from the database, not cached: they are per person and exist only for user-level settings.
        if (definition.Allows(SettingScope.User) && CurrentUserId() is { } userId
            && await tenantSettings.FindUserValueAsync(userId, definition.Key, cancellationToken) is { } userJson)
        {
            if (definition.TryRead(userJson, out var userValue))
            {
                return userValue;
            }

            Log.Configuration.StoredSettingIgnored(logger, definition.Key, nameof(SettingSource.User));
        }

        var snapshot = await snapshots.GetAsync(cancellationToken);
        foreach (var (level, values) in StoredLevels(definition, snapshot))
        {
            if (!values.TryGetValue(definition.Key, out var json))
            {
                continue;
            }

            if (definition.TryRead(json, out var value))
            {
                return value;
            }

            Log.Configuration.StoredSettingIgnored(logger, definition.Key, level.ToString());
        }

        return definition.Default;
    }

    public async Task<string?> GetSecretAsync(SecretSettingDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var snapshot = await snapshots.GetAsync(cancellationToken);
        foreach (var (level, values) in StoredLevels(definition, snapshot))
        {
            if (!values.TryGetValue(definition.Key, out var json))
            {
                continue;
            }

            if (TryUnprotect(json, out var plain))
            {
                return plain;
            }

            Log.Configuration.StoredSettingIgnored(logger, definition.Key, level.ToString());
        }

        return null;
    }

    /// <summary>Tenant then platform values, limited to the levels the definition allows.</summary>
    private IEnumerable<(SettingSource Level, IReadOnlyDictionary<string, string> Values)> StoredLevels(SettingDefinition definition, SettingsSnapshot snapshot)
    {
        if (definition.Allows(SettingScope.Tenant) && tenantContext.IsResolved)
        {
            yield return (SettingSource.Tenant, snapshot.Tenant);
        }

        if (definition.Allows(SettingScope.Platform))
        {
            yield return (SettingSource.Platform, snapshot.Platform);
        }
    }

    private Guid? CurrentUserId() =>
        tenantContext.IsResolved && currentUser.ActorType == ActorType.User ? currentUser.UserId : null;

    private bool TryUnprotect(string json, out string? plain)
    {
        plain = null;
        try
        {
            var protectedValue = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrEmpty(protectedValue))
            {
                return false;
            }

            plain = secrets.Unprotect(protectedValue);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException)
        {
            return false;
        }
    }
}
