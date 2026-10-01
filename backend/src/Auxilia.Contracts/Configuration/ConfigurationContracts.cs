using System.Text.Json;

namespace Auxilia.Contracts.Configuration;

/// <summary>
/// A setting of the tenant as the System edits it (<c>GET /settings</c>, F23): every value level with its source.
/// <see cref="Kind"/> is <c>boolean</c>, <c>integer</c>, <c>number</c>, <c>string</c>, <c>choice</c> (one of
/// <see cref="Choices"/>) or <c>secret</c>; secret values are never returned, only whether a level stores one.
/// </summary>
public sealed record SettingResponse(
    string Key,
    string Module,
    string Kind,
    IReadOnlyList<string>? Choices,
    JsonElement? DefaultValue,
    JsonElement? PlatformValue,
    JsonElement? TenantValue,
    JsonElement? EffectiveValue,
    string Source,
    bool HasTenantValue);

/// <summary><c>PUT /settings/{key}</c>: the tenant-level value as JSON (a secret is a JSON string).</summary>
public sealed record SetSettingRequest(JsonElement Value);

/// <summary>
/// Branding of the tenant for every visitor (<c>GET /branding</c>, anonymous). Colours are hex; the UI derives accessible
/// tokens from them. An image version is present when the tenant has that image (its URL carries it, so a new image is
/// never served from a stale cache).
/// </summary>
public sealed record BrandingResponse(
    string AppName,
    bool UseAppName,
    string ThemeFill,
    string PrimaryColor,
    string AccentColor,
    BrandingBackgroundResponse Background,
    string? LogoVersion);

public sealed record BrandingBackgroundResponse(string Kind, string StartColor, string EndColor, string Color, string? ImageVersion);
