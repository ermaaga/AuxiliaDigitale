using System.Text.Json;

using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F20, F27, Q41): <c>EntityConfigurations</c> (a list of <c>PropertyName</c>, <c>PropertyType</c>,
/// <c>VisibleOnGrid</c> per legacy entity) → <c>configuration.custom_field_definitions</c>, keeping the key so the values
/// already copied (<c>custom_fields</c>) match; boolean CAF/PATRONATO fields count on the employee dashboard.
/// </summary>
internal sealed class CustomFieldsStep : ILegacyImportStep
{
    public const string Table = "EntityConfigurations";

    private static readonly string[] DashboardCounters = ["CAF", "PATRONATO"];

    public string Name => "custom fields";

    /// <summary>Legacy entity name → custom field entity of the new modules.</summary>
    public static string? Entity(string legacy) => legacy switch
    {
        "User" => "client",
        "Subscription" => "case",
        "Appointment" => "appointment",
        "UserDocument" => "document",
        "Request" => "request",
        _ => null,
    };

    /// <summary>Legacy property type → <see cref="CustomFieldType"/>.</summary>
    public static CustomFieldType Type(string? legacy) => legacy?.Trim().ToLowerInvariant() switch
    {
        "boolean" or "bool" => CustomFieldType.Boolean,
        "number" or "int" or "integer" or "decimal" or "double" => CustomFieldType.Number,
        "date" or "datetime" => CustomFieldType.Date,
        _ => CustomFieldType.Text,
    };

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var existing = (await context.Tenant.Set<CustomFieldDefinition>().ToListAsync(cancellationToken))
            .Select(definition => (definition.EntityType, definition.Key.ToUpperInvariant())).ToHashSet();
        var result = context.Report.For(Table);
        foreach (var legacy in await context.Legacy.EntityConfigurations.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (Entity(legacy.EntityName) is not { } entity)
            {
                context.Report.Skip(Table, legacy.Id, $"no custom fields for {legacy.EntityName} in the new system");
                continue;
            }

            List<LegacyField>? fields;
            try
            {
                fields = JsonSerializer.Deserialize<List<LegacyField>>(legacy.Configuration);
            }
            catch (JsonException)
            {
                fields = null;
            }

            if (fields is null)
            {
                context.Report.Skip(Table, legacy.Id, "configuration not readable");
                continue;
            }

            var order = 0;
            foreach (var field in fields.Where(field => !string.IsNullOrWhiteSpace(field.PropertyName)))
            {
                order += 10;
                var key = field.PropertyName!.Trim();
                if (!existing.Add((entity, key.ToUpperInvariant())))
                {
                    continue;
                }

                var type = Type(field.PropertyType);
                var spec = new CustomFieldSpec(
                    LegacyText.Fit(key, CustomFieldDefinition.LabelMaxLength, out _), type, Options: null, IsRequired: false, GroupName: null, BadgeColor: null,
                    field.VisibleOnGrid, DashboardCounter: type == CustomFieldType.Boolean && DashboardCounters.Contains(key, StringComparer.OrdinalIgnoreCase),
                    Math.Min(order, CustomFieldDefinition.MaxOrder));
                var created = CustomFieldDefinition.Create(context.Ids.NewId(), entity, key, spec);
                if (created.IsFailure)
                {
                    context.Report.Warn(Table, legacy.Id, "a field name is not a valid key: left out");
                    continue;
                }

                context.Tenant.Add(created.Value);
                result.Created++;
            }
        }
    }

    private sealed record LegacyField(string? PropertyName, string? PropertyType, bool VisibleOnGrid);
}
