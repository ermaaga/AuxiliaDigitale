using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-03 (F08, F33): <c>MembershipTypes</c> → <c>cases.service_categories</c>, <c>Memberships</c> → <c>cases.services</c>,
/// <c>MembershipFolderTemplates</c> → <c>cases.service_folders</c> (parents before children).
/// </summary>
internal sealed class ServiceCatalogStep : ILegacyImportStep
{
    public const string CategoriesTable = "MembershipTypes";
    public const string ServicesTable = "Memberships";
    public const string FoldersTable = "MembershipFolderTemplates";

    public string Name => "service catalog";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await CategoriesAsync(context, cancellationToken);
        await ServicesAsync(context, cancellationToken);
        await FoldersAsync(context, cancellationToken);
    }

    /// <summary>A name unique among <paramref name="taken"/> (case-insensitive): the legacy id is appended to a duplicate.</summary>
    public static string UniqueName(string name, int legacyId, int maxLength, ISet<string> taken, out bool renamed)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);

        var trimmed = name.Trim();
        renamed = taken.Contains(trimmed);
        if (renamed)
        {
            var suffix = $" ({legacyId})";
            trimmed = (trimmed.Length + suffix.Length > maxLength ? trimmed[..(maxLength - suffix.Length)] : trimmed) + suffix;
        }

        return trimmed;
    }

    private static async Task CategoriesAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        var existing = await context.Tenant.Set<ServiceCategory>().ToDictionaryAsync(category => category.Id, cancellationToken);
        var active = new HashSet<string>(existing.Values.Where(category => category.IsActive).Select(category => category.Name), StringComparer.OrdinalIgnoreCase);
        var result = context.Report.For(CategoriesTable);
        foreach (var legacy in await context.Legacy.MembershipTypes.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            var mapped = context.Ids.Find(CategoriesTable, legacy.Id) is { } id && existing.TryGetValue(id, out var found) ? found : null;
            if (mapped is not null && mapped.IsActive)
            {
                active.Remove(mapped.Name);
            }

            var renamed = false;
            var name = legacy.IsActive ? UniqueName(legacy.Name, legacy.Id, ServiceCategory.NameMaxLength, active, out renamed) : legacy.Name.Trim();
            if (renamed)
            {
                context.Report.Warn(CategoriesTable, legacy.Id, "name already used by an active category: legacy id appended");
            }

            if (mapped is not null)
            {
                if (mapped.Update(name, legacy.Description, legacy.IsActive).IsSuccess)
                {
                    result.Updated++;
                }
                else
                {
                    context.Report.Warn(CategoriesTable, legacy.Id, "values not valid: previous values kept");
                }
            }
            else
            {
                var newId = context.Ids.NewId();
                var created = ServiceCategory.Create(newId, name, legacy.Description);
                if (created.IsFailure)
                {
                    created = ServiceCategory.Create(newId, name, description: null);
                    context.Report.Warn(CategoriesTable, legacy.Id, "description not valid: left out");
                }

                if (created.IsFailure)
                {
                    context.Report.Skip(CategoriesTable, legacy.Id, "the category name is not valid");
                    continue;
                }

                if (!legacy.IsActive)
                {
                    created.Value.Update(name, created.Value.Description, isActive: false);
                }

                context.Tenant.Add(created.Value);
                context.Ids.Add(CategoriesTable, legacy.Id, newId);
                result.Created++;
            }

            if (legacy.IsActive)
            {
                active.Add(name);
            }
        }
    }

    private static async Task ServicesAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        var existing = await context.Tenant.Set<Service>().ToDictionaryAsync(service => service.Id, cancellationToken);
        var employeeSpecializations = await context.Tenant.Set<Specialization>().Where(specialization => specialization.Role == TenantRole.Employee)
            .Select(specialization => specialization.Id).ToListAsync(cancellationToken);
        var taken = new HashSet<string>(existing.Values.Select(service => service.Name), StringComparer.OrdinalIgnoreCase);
        var result = context.Report.For(ServicesTable);
        foreach (var legacy in await context.Legacy.Memberships.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            var mapped = context.Ids.Find(ServicesTable, legacy.Id) is { } id && existing.TryGetValue(id, out var found) ? found : null;
            if (mapped is not null)
            {
                taken.Remove(mapped.Name);
            }

            var name = UniqueName(legacy.Name, legacy.Id, Service.NameMaxLength, taken, out var renamed);
            if (renamed)
            {
                context.Report.Warn(ServicesTable, legacy.Id, "name already used by another service: legacy id appended");
            }

            Guid? category = null;
            if (legacy.MembershipTypeId is { } legacyCategory && (category = context.Ids.Find(CategoriesTable, legacyCategory)) is null)
            {
                context.Report.Warn(ServicesTable, legacy.Id, "category not migrated: service without category");
            }

            Guid? specialization = null;
            if (legacy.RoleSpecializationId is { } legacySpecialization)
            {
                specialization = context.Ids.Find(SpecializationsStep.Table, legacySpecialization);
                if (specialization is not { } specializationId || !employeeSpecializations.Contains(specializationId))
                {
                    context.Report.Warn(ServicesTable, legacy.Id, "specialization not migrated or not of the Employee role: left out");
                    specialization = null;
                }
            }

            var duration = Math.Clamp(legacy.DurationDays, 1, Service.MaxDurationDays);
            if (duration != legacy.DurationDays)
            {
                context.Report.Warn(ServicesTable, legacy.Id, $"duration out of range: set to {duration} days");
            }

            var details = new ServiceDetails(name, legacy.Description, decimal.Round(legacy.Price, 2), duration, category, specialization, legacy.IsActive);
            if (mapped is not null)
            {
                if (mapped.Update(details).IsSuccess)
                {
                    result.Updated++;
                }
                else
                {
                    context.Report.Warn(ServicesTable, legacy.Id, "values not valid: previous values kept");
                }

                taken.Add(mapped.Name);
                continue;
            }

            var newId = context.Ids.NewId();
            var created = Service.Create(newId, details);
            if (created.IsFailure && created.Error!.ValidationErrors?.Keys.SequenceEqual(["description"]) == true)
            {
                context.Report.Warn(ServicesTable, legacy.Id, "description not valid: left out");
                created = Service.Create(newId, details with { Description = null });
            }

            if (created.IsFailure)
            {
                context.Report.Skip(ServicesTable, legacy.Id, $"the service is not valid ({string.Join(", ", created.Error!.ValidationErrors?.Keys ?? [])})");
                continue;
            }

            context.Tenant.Add(created.Value);
            context.Ids.Add(ServicesTable, legacy.Id, newId);
            taken.Add(name);
            result.Created++;
        }
    }

    private static async Task FoldersAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        var existing = await context.Tenant.Set<ServiceFolder>().ToDictionaryAsync(folder => folder.Id, cancellationToken);
        var rows = await context.Legacy.MembershipFolderTemplates.OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(row => row.Id);
        var result = context.Report.For(FoldersTable);

        // Parents first: a folder's depth is the length of its chain of parents (cycles are reported, not followed).
        foreach (var legacy in rows.OrderBy(row => Depth(row, byId)).ThenBy(row => row.Id))
        {
            if (Depth(legacy, byId) > ServiceFolder.MaxDepth)
            {
                context.Report.Skip(FoldersTable, legacy.Id, "folder too deep or in a cycle of parents");
                continue;
            }

            if (context.Ids.Find(ServicesTable, legacy.MembershipId) is not { } serviceId)
            {
                context.Report.Skip(FoldersTable, legacy.Id, "service not migrated");
                continue;
            }

            Guid? parent = null;
            if (legacy.ParentId is { } legacyParent && (parent = context.Ids.Find(FoldersTable, legacyParent)) is null)
            {
                context.Report.Skip(FoldersTable, legacy.Id, "parent folder not migrated");
                continue;
            }

            if (context.Ids.Find(FoldersTable, legacy.Id) is { } id && existing.TryGetValue(id, out var folder))
            {
                if (folder.Rename(legacy.Name).IsSuccess)
                {
                    folder.MoveTo(legacy.SortOrder);
                    result.Updated++;
                }

                continue;
            }

            var newId = context.Ids.NewId();
            var created = ServiceFolder.Create(newId, serviceId, parent, legacy.Name, legacy.SortOrder);
            if (created.IsFailure)
            {
                context.Report.Skip(FoldersTable, legacy.Id, "the folder name is not valid");
                continue;
            }

            context.Tenant.Add(created.Value);
            context.Ids.Add(FoldersTable, legacy.Id, newId);
            result.Created++;
        }
    }

    private static int Depth(LegacyMembershipFolderTemplate row, Dictionary<int, LegacyMembershipFolderTemplate> byId)
    {
        var depth = 1;
        for (var current = row; current.ParentId is { } parent && byId.TryGetValue(parent, out var next); current = next)
        {
            if (++depth > ServiceFolder.MaxDepth)
            {
                return int.MaxValue;
            }
        }

        return depth;
    }
}
