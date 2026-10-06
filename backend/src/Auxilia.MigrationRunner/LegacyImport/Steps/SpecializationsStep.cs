using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>E-02: <c>RoleSpecializations</c> → <c>directory.specializations</c> (F12).</summary>
internal sealed class SpecializationsStep : ILegacyImportStep
{
    public const string Table = "RoleSpecializations";

    public string Name => "specializations";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var roleNames = await context.Legacy.Roles.ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);
        var existing = await context.Tenant.Set<Specialization>().ToDictionaryAsync(specialization => specialization.Id, cancellationToken);
        var active = existing.Values.Where(specialization => specialization.IsActive)
            .ToDictionary(specialization => (specialization.Role, specialization.Name.ToUpperInvariant()), specialization => specialization.Id);
        var result = context.Report.For(Table);

        foreach (var legacy in await context.Legacy.RoleSpecializations.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (!TenantRoles.TryParse(roleNames.GetValueOrDefault(legacy.RoleId), out var role) || !Specialization.Roles.Contains(role))
            {
                context.Report.Skip(Table, legacy.Id, "role is not Client or Employee");
                continue;
            }

            var mapped = context.Ids.Find(Table, legacy.Id);
            var key = (role, legacy.Name.Trim().ToUpperInvariant());
            if (legacy.IsActive && active.TryGetValue(key, out var holder) && holder != mapped)
            {
                context.Report.Skip(Table, legacy.Id, "an active specialization of the role has the same name");
                continue;
            }

            var spec = new SpecializationSpec(legacy.Name, legacy.Description, legacy.Email, legacy.WorkNumber, legacy.PrivateSubscriptions);
            if (mapped is { } id && existing.TryGetValue(id, out var current))
            {
                if (Apply(context, legacy.Id, spec, current.Update).IsSuccess)
                {
                    result.Updated++;
                }
                else
                {
                    context.Report.Warn(Table, legacy.Id, "the specialization name is not valid: previous values kept");
                }

                if (!legacy.IsActive)
                {
                    current.Deactivate();
                }

                continue;
            }

            var newId = context.Ids.NewId();
            Specialization? created = null;
            var applied = Apply(context, legacy.Id, spec, candidate =>
            {
                var attempt = Specialization.Create(newId, role, candidate);
                created = attempt.IsSuccess ? attempt.Value : null;
                return attempt.IsSuccess ? Result.Success() : Result.Failure(attempt.Error!);
            });
            if (applied.IsFailure || created is null)
            {
                context.Report.Skip(Table, legacy.Id, "the specialization name is not valid");
                continue;
            }

            if (!legacy.IsActive)
            {
                created.Deactivate();
            }
            else
            {
                active[key] = newId;
            }

            context.Tenant.Add(created);
            context.Ids.Add(Table, legacy.Id, newId);
            result.Created++;
        }
    }

    /// <summary>Applies the values; an invalid optional value (description, e-mail, phone) is left out with a warning.</summary>
    private static Result Apply(LegacyImportContext context, int legacyId, SpecializationSpec spec, Func<SpecializationSpec, Result> apply)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = apply(spec);
            var field = result.Error?.ValidationErrors?.Keys.FirstOrDefault();
            if (result.IsSuccess || attempt == 3 || field is not ("description" or "email" or "workPhone"))
            {
                return result;
            }

            context.Report.Warn(Table, legacyId, $"{field} not valid: left out");
            spec = field switch
            {
                "description" => spec with { Description = null },
                "email" => spec with { Email = null },
                _ => spec with { WorkPhone = null },
            };
        }
    }
}
