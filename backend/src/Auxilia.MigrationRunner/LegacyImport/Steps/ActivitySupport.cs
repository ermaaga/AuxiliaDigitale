using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>Lookups the E-05 steps share: migrated users, their person and roles.</summary>
internal sealed class MigratedUsers
{
    private readonly Dictionary<Guid, (Guid PersonId, IReadOnlyCollection<TenantRole> Roles)> users;
    private readonly HashSet<Guid> clients;

    private MigratedUsers(Dictionary<Guid, (Guid PersonId, IReadOnlyCollection<TenantRole> Roles)> users, HashSet<Guid> clients)
    {
        this.users = users;
        this.clients = clients;
    }

    public static async Task<MigratedUsers> LoadAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        var users = (await context.Tenant.Set<User>().AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(user => user.Id, user => (user.PersonId, user.Roles));
        var clients = (await context.Tenant.Set<ClientProfile>().AsNoTracking().Select(profile => profile.Id).ToListAsync(cancellationToken)).ToHashSet();
        return new MigratedUsers(users, clients);
    }

    /// <summary>The new user id of a legacy user, when it was migrated.</summary>
    public Guid? User(LegacyImportContext context, int legacyUserId) =>
        context.Ids.Find(UsersStep.Table, legacyUserId) is { } id && users.ContainsKey(id) ? id : null;

    /// <summary>The person id of a legacy user that was migrated as a client.</summary>
    public Guid? Client(LegacyImportContext context, int legacyUserId) =>
        User(context, legacyUserId) is { } id && users[id].PersonId is var person && clients.Contains(person) ? person : null;

    public bool HasRole(Guid userId, TenantRole role) => users.TryGetValue(userId, out var user) && user.Roles.Contains(role);

    /// <summary>An Administrator, the author of the legacy office replies (requests to nobody in particular).</summary>
    public Guid? AnyAdministrator(LegacyImportContext context, IEnumerable<int> legacyUserIds) =>
        legacyUserIds.Order().Select(id => User(context, id)).FirstOrDefault(id => id is { } user && HasRole(user, TenantRole.Administrator));
}

/// <summary>Small value rules of E-05.</summary>
internal static class LegacyText
{
    /// <summary>Trimmed and cut to <paramref name="maxLength"/>; <paramref name="cut"/> tells whether it was longer.</summary>
    public static string Fit(string? value, int maxLength, out bool cut)
    {
        var text = value?.Trim() ?? string.Empty;
        cut = text.Length > maxLength;
        return cut ? text[..maxLength] : text;
    }
}
