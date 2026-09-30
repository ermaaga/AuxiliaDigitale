using System.Text.Json;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Configuration;

/// <summary>
/// Stores and removes setting values at one level (ARCHITECTURE §7.1). Platform and tenant levels are changed by the
/// System (authorization with P2-03/P2-06), the user level by the current user for themselves. Secret values are
/// protected before they are stored. Caches are invalidated after the commit.
/// </summary>
public interface ISettingsManager
{
    Task<Result> SetAsync(SetSetting request, CancellationToken cancellationToken);

    /// <summary>Removes the value stored at the level, so the next level applies again; nothing stored is not an error.</summary>
    Task<Result> ResetAsync(ResetSetting request, CancellationToken cancellationToken);
}

/// <param name="Level">A single level: <see cref="SettingScope.Platform"/>, <see cref="SettingScope.Tenant"/> or <see cref="SettingScope.User"/>.</param>
/// <param name="Value">The value as JSON (a secret is a JSON string with the plain value).</param>
public sealed record SetSetting(string Key, SettingScope Level, JsonElement Value);

public sealed record ResetSetting(string Key, SettingScope Level);
