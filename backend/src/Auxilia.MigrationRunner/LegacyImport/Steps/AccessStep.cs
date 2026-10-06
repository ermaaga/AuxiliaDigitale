using System.Text.Json;

using Auxilia.Application.Abstractions.Modules;
using Auxilia.Domain.Configuration;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F21, F22, Q40): <c>ModuleConfigurations</c> and <c>PageConfigurations</c> disabled for a role → the
/// permissions of that page taken from the role (<c>identity.role_permissions</c>; the permission sync never gives them
/// back), and the <c>ConfigurationGrid</c> of a page → the role's layout of the matching grid
/// (<c>configuration.grid_layouts</c>: the legacy columns visible in the legacy order, the others hidden). Pages that are
/// enabled, or have no row, keep the default grants of the new modules.
/// </summary>
internal sealed class AccessStep : ILegacyImportStep
{
    public const string ModulesTable = "ModuleConfigurations";
    public const string PagesTable = "PageConfigurations";

    /// <summary>Legacy page (or module path) → the permissions behind it.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PagePermissions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = ["reporting.dashboard.view"],
        ["Clients"] = ["directory.clients.view", "directory.clients.manage", "directory.clients.assign", "directory.clients.delete", "directory.clients.credentials"],
        ["Employees"] = ["directory.employees.view", "directory.employees.manage"],
        ["Requests"] = ["engagement.requests.view", "engagement.requests.manage", "engagement.requests.delete"],
        ["UserRequests"] = ["engagement.requests.view", "engagement.requests.manage", "engagement.requests.delete"],
        ["RegistrationRequests"] = ["directory.registrations.review"],
        ["Subscriptions"] = ["cases.cases.view", "cases.cases.manage", "cases.cases.delete"],
        ["Subscription"] = ["cases.cases.view", "cases.cases.manage", "cases.cases.delete"],
        ["Memberships"] = ["cases.services.view", "cases.services.manage"],
        ["Appointments"] = ["scheduling.appointments.view", "scheduling.appointments.manage"],
        ["Documents"] = ["documents.files.view", "documents.files.manage"],
        ["Sessions"] = ["identity.sessions.view", "identity.sessions.revoke"],
    };

    /// <summary>Legacy pages with no equivalent permission: platform logs (D-17), workout plans (D-09), the "all clients" toggle.</summary>
    public static readonly IReadOnlySet<string> WithoutEquivalent = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Logs", "WorkoutPlans", "AllClients" };

    /// <summary>Legacy page → grid key and legacy property → column key.</summary>
    public static readonly IReadOnlyDictionary<string, (string Grid, IReadOnlyDictionary<string, string> Columns)> PageGrids =
        new Dictionary<string, (string, IReadOnlyDictionary<string, string>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Clients"] = ("directory.clients", Columns(
                ("FullName", "firstName"), ("Surname", "lastName"), ("Email", "email"), ("Username", "userName"), ("Phone", "phone"),
                ("FiscalCode", "fiscalCode"), ("AssignedEmployee.FullName", "employee"), ("IsActive", "canSignIn"))),
            ["Employees"] = ("directory.employees", Columns(
                ("FullName", "firstName"), ("Surname", "lastName"), ("Username", "userName"), ("Email", "email"), ("Phone", "phone"),
                ("IsActive", "status"), ("UserRoleSpecializations", "specializations"))),
            ["Subscriptions"] = ("cases.cases", Columns(
                ("User.FullName", "client"), ("Membership.Name", "service"), ("StartDate", "startedOn"), ("EndDate", "expiresOn"),
                ("AmountPaid", "amountPaid"), ("Status", "status"), ("RoleSpecialization.Name", "specialization"))),
            ["Memberships"] = ("cases.services", Columns(
                ("Name", "name"), ("Description", "description"), ("MembershipType.Name", "category"), ("RoleSpecialization.Name", "specialization"),
                ("Price", "price"), ("DurationDays", "durationDays"), ("IsActive", "status"))),
            ["Appointments"] = ("scheduling.appointments", Columns(
                ("Client.FullName", "client"), ("Employee.FullName", "employee"), ("ScheduledDate", "startsAt"), ("DurationMinutes", "duration"),
                ("ShowInGlobalCalendare", "showInGlobalCalendar"), ("Status", "status"))),
            ["Requests"] = ("engagement.requests", Columns(
                ("CreatedAt", "sentAt"), ("Sender.FullName", "sender"), ("Receiver.FullName", "recipient"), ("Type", "type"), ("Subject", "subject"),
                ("Status", "status"))),
        };

    private readonly IModuleRegistry modules;

    public AccessStep(IModuleRegistry modules) => this.modules = modules;

    public string Name => "permissions and grids";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var grants = await context.Tenant.Set<RoleGrant>().ToListAsync(cancellationToken);
        var moduleRows = await context.Legacy.ModuleConfigurations.OrderBy(row => row.Id).ToListAsync(cancellationToken);
        foreach (var row in moduleRows)
        {
            Revoke(context, grants, ModulesTable, row.Id, row.ModulePath, row.Role, row.IsEnabled);
        }

        var layouts = await context.Tenant.Set<GridLayout>().ToListAsync(cancellationToken);
        foreach (var row in await context.Legacy.PageConfigurations.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            Revoke(context, grants, PagesTable, row.Id, row.PageName, row.Role, row.IsEnabled);
            if (row.IsEnabled && !string.IsNullOrWhiteSpace(row.ConfigurationGrid))
            {
                Grid(context, layouts, row);
            }
        }
    }

    /// <summary>
    /// The layout of a grid from the legacy columns: the listed ones visible in the legacy order, the other columns the
    /// legacy grid could show hidden (when they can be), columns new in this system with their default.
    /// </summary>
    public static IReadOnlyList<GridLayoutColumn> Layout(GridDefinition grid, IReadOnlyDictionary<string, string> columns, IEnumerable<string> legacyProperties)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(columns);

        var keys = grid.Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        var visible = legacyProperties.Select(property => columns.GetValueOrDefault(property)).OfType<string>().Where(keys.Contains).Distinct().ToList();
        return
        [
            .. visible.Select(key => new GridLayoutColumn(key, Visible: true)),
            .. grid.Columns.Where(column => !visible.Contains(column.Key)).Select(column => new GridLayoutColumn(
                column.Key, Visible: !column.CanHide || (column.VisibleByDefault && !columns.Values.Contains(column.Key, StringComparer.Ordinal)))),
        ];
    }

    private static Dictionary<string, string> Columns(params (string Legacy, string Key)[] pairs) =>
        pairs.ToDictionary(pair => pair.Legacy, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static void Revoke(LegacyImportContext context, List<RoleGrant> grants, string table, int legacyId, string page, string legacyRole, bool enabled)
    {
        if (!TenantRoles.TryParse(legacyRole, out var role))
        {
            // SystemConfigurator and the old "SystemConfiguration" entries (D-18).
            context.Report.For(table).Excluded++;
            return;
        }

        if (!PagePermissions.TryGetValue(page, out var permissions))
        {
            if (WithoutEquivalent.Contains(page))
            {
                context.Report.For(table).Excluded++;
            }
            else
            {
                context.Report.Warn(table, legacyId, $"page {page} has no equivalent permissions: left out");
            }

            return;
        }

        if (enabled)
        {
            return;
        }

        var revoked = grants.Where(grant => grant.Role == role && permissions.Contains(grant.PermissionCode, StringComparer.Ordinal)).ToArray();
        foreach (var grant in revoked)
        {
            context.Tenant.Remove(grant);
            grants.Remove(grant);
        }

        if (revoked.Length > 0)
        {
            context.Report.For(table).Updated++;
        }
    }

    private void Grid(LegacyImportContext context, List<GridLayout> layouts, LegacyPageConfiguration row)
    {
        if (!PageGrids.TryGetValue(row.PageName, out var target) || !modules.Grids.TryGetValue(target.Grid, out var grid)
            || !TenantRoles.TryParse(row.Role, out var role) || !grid.Roles.Contains(role))
        {
            return;
        }

        List<LegacyGridColumn>? legacyColumns;
        try
        {
            legacyColumns = JsonSerializer.Deserialize<List<LegacyGridColumn>>(row.ConfigurationGrid!);
        }
        catch (JsonException)
        {
            legacyColumns = null;
        }

        if (legacyColumns is null)
        {
            context.Report.Warn(PagesTable, row.Id, "grid configuration not readable: default layout kept");
            return;
        }

        var columns = Layout(grid, target.Columns, legacyColumns.Select(column => column.Property ?? string.Empty));
        var roleName = role.ToString();
        if (layouts.FirstOrDefault(layout => layout.GridKey == grid.Key && layout.Role == roleName) is { } existing)
        {
            existing.Replace(columns);
            return;
        }

        var created = GridLayout.Create(context.Ids.NewId(), grid.Key, roleName, columns);
        if (created.IsSuccess)
        {
            context.Tenant.Add(created.Value);
            layouts.Add(created.Value);
            context.Report.For("grid layouts").Created++;
        }
    }

    private sealed record LegacyGridColumn(string? Label, string? Property);
}
