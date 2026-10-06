using System.Globalization;
using System.Text.RegularExpressions;

using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Cases;
using Auxilia.Application.Configuration;
using Auxilia.Application.Directory;
using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F23, ARCHITECTURE §7.2): <c>SystemConfigurations</c> → <c>configuration.settings</c> (typed values checked by
/// their <see cref="SettingDefinition{T}"/>) and the login background image → <c>configuration.branding_assets</c>.
/// Keys removed on purpose are ignored; unknown keys are reported.
/// </summary>
internal sealed partial class SettingsStep : ILegacyImportStep
{
    public const string Table = "SystemConfigurations";

    /// <summary>Legacy keys with no tenant setting (D-06, D-14, D-15, infrastructure, ARCHITECTURE §7.2).</summary>
    public static readonly IReadOnlySet<string> Dropped = new HashSet<string>(StringComparer.Ordinal)
    {
        "DefaultPassword", "ReCaptcha", "UseLocalCache", "UseQueueForDocuments", "EnableSubscriptionExpiryService", "InitDatabase", "SaveLog", "AuditLog",
        "LogBlobStorage", "SessionTimeout", "ThemeSolid", "ThemeSecondary", "BackgroundGradient",
    };

    public string Name => "settings";

    /// <summary>The two colours of a CSS gradient (<c>linear-gradient(135deg, #667eea 0%, #764ba2 100%)</c>).</summary>
    public static (string Start, string End)? GradientColors(string? css)
    {
        var colors = HexColor().Matches(css ?? string.Empty).Select(match => match.Value).ToArray();
        return colors.Length >= 2 ? (colors[0], colors[1]) : null;
    }

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var legacy = (await context.Legacy.SystemConfigurations.ToListAsync(cancellationToken))
            .GroupBy(setting => setting.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(setting => setting.UpdatedAt).First(), StringComparer.Ordinal);
        var stored = await context.Tenant.Set<TenantSetting>().ToDictionaryAsync(setting => setting.Key, StringComparer.Ordinal, cancellationToken);
        var writer = new Writer(context, stored);

        foreach (var (key, row) in legacy)
        {
            var value = row.Value.Trim();
            switch (key)
            {
                case "RegistrationEnabled":
                    writer.Bool(row, DirectorySettings.RegistrationEnabled, value);
                    break;
                case "SendRegistrationConfirmationEmail":
                    writer.Bool(row, DirectorySettings.RegistrationSendConfirmationEmail, value);
                    break;
                case "RegistrationLanguage":
                    writer.Set(row, DirectorySettings.RegistrationDefaultLanguage, value.ToLowerInvariant());
                    break;
                case "AutoSubscriptionExpiry":
                    writer.Bool(row, CasesSettings.ExpiryEnabled, value);
                    break;
                case "SubscriptionExpiringDays":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                    {
                        writer.Set(row, CasesSettings.ExpiryExpiringDays, days);
                    }
                    else
                    {
                        writer.Invalid(row);
                    }

                    break;
                case "UseAppName":
                    // The legacy header hides the name only for the exact text "False".
                    writer.Set(row, BrandingSettings.UseAppName, value != "False");
                    break;
                case "ThemeType":
                    var gradient = value == "gradient";
                    writer.Set(row, BrandingSettings.ThemeFill, gradient ? BrandingFill.Gradient : BrandingFill.Solid);
                    if (gradient)
                    {
                        Color(writer, legacy, "ThemePrimary", BrandingSettings.PrimaryColor);
                        Color(writer, legacy, "ThemeSecondary", BrandingSettings.AccentColor);
                    }
                    else
                    {
                        Color(writer, legacy, "ThemeSolid", BrandingSettings.PrimaryColor);
                    }

                    break;
                case "ThemePrimary":
                    if (!legacy.ContainsKey("ThemeType"))
                    {
                        Color(writer, legacy, "ThemePrimary", BrandingSettings.PrimaryColor);
                        Color(writer, legacy, "ThemeSecondary", BrandingSettings.AccentColor);
                    }

                    break;
                case "BackgroundType":
                    writer.Set(row, BrandingSettings.BackgroundType, value switch
                    {
                        "image" => BackgroundKind.Image,
                        "color" => BackgroundKind.Solid,
                        _ => BackgroundKind.Gradient,
                    });
                    if (legacy.TryGetValue("BackgroundGradient", out var css) && GradientColors(css.Value) is { } colors)
                    {
                        writer.Set(css, BrandingSettings.BackgroundStartColor, colors.Start);
                        writer.Set(css, BrandingSettings.BackgroundEndColor, colors.End);
                    }

                    break;
                case "BackgroundColor":
                    writer.Set(row, BrandingSettings.BackgroundColor, value);
                    break;
                case "BackgroundImage":
                    await BackgroundAsync(context, row, cancellationToken);
                    break;
                case var dropped when Dropped.Contains(dropped):
                    context.Report.For(Table).Excluded++;
                    break;
                default:
                    context.Report.Warn(Table, row.Id, $"unknown setting {key}: left out");
                    break;
            }
        }
    }

    private static void Color(Writer writer, Dictionary<string, LegacySystemConfiguration> legacy, string key, SettingDefinition<string> definition)
    {
        if (legacy.TryGetValue(key, out var row))
        {
            writer.Set(row, definition, row.Value.Trim());
        }
    }

    private static async Task BackgroundAsync(LegacyImportContext context, LegacySystemConfiguration row, CancellationToken cancellationToken)
    {
        var data = row.Value.Trim();
        var comma = data.IndexOf(',', StringComparison.Ordinal);
        var bytes = new byte[data.Length];
        if (!Convert.TryFromBase64String(comma >= 0 ? data[(comma + 1)..] : data, bytes, out var length))
        {
            context.Report.Warn(Table, row.Id, "background image not readable: left out");
            return;
        }

        var content = bytes[..length];
        var existing = await context.Tenant.Set<BrandingAsset>().SingleOrDefaultAsync(asset => asset.Kind == BrandingAssetKind.Background, cancellationToken);
        if (existing is not null)
        {
            if (existing.Replace(content).IsSuccess)
            {
                context.Report.For(Table).Updated++;
            }
            else
            {
                context.Report.Warn(Table, row.Id, "background image refused (type or size): left out");
            }

            return;
        }

        var created = BrandingAsset.Create(context.Ids.NewId(), BrandingAssetKind.Background, content);
        if (created.IsFailure)
        {
            context.Report.Warn(Table, row.Id, "background image refused (type or size): left out");
            return;
        }

        context.Tenant.Add(created.Value);
        context.Report.For(Table).Created++;
    }

    [GeneratedRegex("#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\\b", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    /// <summary>Writes typed values into <c>configuration.settings</c>, checked by their definition.</summary>
    private sealed class Writer(LegacyImportContext context, Dictionary<string, TenantSetting> stored)
    {
        public void Bool(LegacySystemConfiguration row, SettingDefinition<bool> definition, string value)
        {
            if (bool.TryParse(value, out var parsed))
            {
                Set(row, definition, parsed);
            }
            else
            {
                Invalid(row);
            }
        }

        public void Set<T>(LegacySystemConfiguration row, SettingDefinition<T> definition, T value)
            where T : notnull
        {
            var json = definition.Write(value);
            if (!definition.TryRead(json, out _))
            {
                Invalid(row);
                return;
            }

            var result = context.Report.For(Table);
            if (stored.TryGetValue(definition.Key, out var setting))
            {
                if (setting.JsonValue != json)
                {
                    setting.SetValue(json);
                    result.Updated++;
                }

                return;
            }

            setting = new TenantSetting(context.Ids.NewId(), definition.Key, json);
            context.Tenant.Add(setting);
            stored[definition.Key] = setting;
            result.Created++;
        }

        public void Invalid(LegacySystemConfiguration row) => context.Report.Warn(Table, row.Id, $"value of {row.Key} not valid: default kept");
    }
}
