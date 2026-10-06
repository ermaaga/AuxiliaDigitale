using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>E-02: <c>UserRoleSpecializations</c> → <c>directory.specialization_members</c> (after users and specializations).</summary>
internal sealed class SpecializationMembersStep : ILegacyImportStep
{
    public const string Table = "UserRoleSpecializations";

    public string Name => "specialization members";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var specializations = await context.Tenant.Set<Specialization>().ToDictionaryAsync(specialization => specialization.Id, cancellationToken);
        var roles = (await context.Tenant.Set<User>().AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(user => user.Id, user => user.Roles);
        var result = context.Report.For(Table);

        foreach (var legacy in await context.Legacy.UserRoleSpecializations.OrderBy(row => row.UserId).ThenBy(row => row.RoleSpecializationId).ToListAsync(cancellationToken))
        {
            // Rows of users or specializations not migrated are already reported by their own tables.
            if (context.Ids.Find(SpecializationsStep.Table, legacy.RoleSpecializationId) is not { } specializationId
                || !specializations.TryGetValue(specializationId, out var specialization)
                || context.Ids.Find(UsersStep.Table, legacy.UserId) is not { } userId
                || !roles.TryGetValue(userId, out var userRoles))
            {
                result.Excluded++;
                continue;
            }

            if (!userRoles.Contains(specialization.Role))
            {
                context.Report.Skip(Table, legacy.UserId, "the user does not have the role of the specialization");
                continue;
            }

            if (specialization.AddMembers([userId], LegacyImportContext.Instant(legacy.AssignedAt)) > 0)
            {
                result.Created++;
            }
        }
    }
}
