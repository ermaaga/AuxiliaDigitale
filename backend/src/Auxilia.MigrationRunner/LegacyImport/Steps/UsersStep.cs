using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-02: <c>Users</c> + <c>UserRoles</c> → <c>directory.people</c>, <c>identity.users</c> (BCrypt hashes as
/// <c>LegacyBcrypt</c>), <c>client_profiles</c> with the assignment, <c>employee_profiles</c>, consents (privacy, and
/// marketing e-mail = true for clients, D-23) and profile pictures. Rules: docs/migration/mapping.md §5.1.
/// </summary>
internal sealed class UsersStep : ILegacyImportStep
{
    public const string Table = "Users";
    public const string SystemConfiguratorRole = "SystemConfigurator";
    public const string ConsentVersion = "legacy";

    /// <summary>Passwords the legacy application set itself (DataSeeder, approval of a registration).</summary>
    public static readonly IReadOnlyList<string> AssignedPasswords = ["password", "admin"];

    private static readonly string[] OptionalFields = ["email", "phone", "fiscalCode", "birthDate"];

    private readonly IPasswordHasher hasher;
    private readonly IImageProcessor images;

    public UsersStep(IPasswordHasher hasher, IImageProcessor images)
    {
        this.hasher = hasher;
        this.images = images;
    }

    public string Name => "users";

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var legacy = context.Legacy;
        var languages = await legacy.Languages.ToDictionaryAsync(language => language.Id, language => language.Code, cancellationToken);
        var roleNames = await legacy.Roles.ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);
        var legacyRoles = (await legacy.UserRoles.ToListAsync(cancellationToken))
            .ToLookup(row => row.UserId, row => roleNames.GetValueOrDefault(row.RoleId) ?? string.Empty);
        var rows = await legacy.Users.OrderBy(user => user.Id).ToListAsync(cancellationToken);
        var defaultPassword = await legacy.SystemConfigurations.Where(setting => setting.Key == "DefaultPassword")
            .Select(setting => setting.Value).FirstOrDefaultAsync(cancellationToken);
        var state = await TenantState.LoadAsync(context, cancellationToken);

        // BCrypt is slow on purpose: only hashes not seen by an earlier run are checked (new users, changed passwords).
        var unchanged = rows.Where(row => context.Ids.Find(Table, row.Id) is { } id && state.Users.TryGetValue(id, out var user) && user.PasswordHash == row.PasswordHash)
            .Select(row => row.Id).ToHashSet();
        var assigned = await AssignedPasswordUsersAsync(rows.Where(row => !unchanged.Contains(row.Id)).ToList(), defaultPassword, cancellationToken);
        var result = context.Report.For(Table);
        var accounts = new List<(LegacyUser Row, User User, IReadOnlyCollection<TenantRole> Roles, bool Created)>();

        foreach (var row in rows)
        {
            var names = legacyRoles[row.Id].ToArray();
            var roles = Roles(names, out var unknown);
            if (unknown.Length > 0)
            {
                context.Report.Warn(Table, row.Id, $"unknown role {string.Join(", ", unknown)}: left out");
            }

            if (roles.Count == 0)
            {
                if (names.Contains(SystemConfiguratorRole, StringComparer.Ordinal))
                {
                    result.Excluded++;
                }
                else
                {
                    context.Report.Skip(Table, row.Id, "the user has no role");
                }

                continue;
            }

            var account = Account(context, state, row, roles, languages, assigned.Contains(row.Id), unchanged.Contains(row.Id));
            if (account is null)
            {
                continue;
            }

            accounts.Add((row, account.Value.User, roles, account.Value.Created));
            if (account.Value.Created)
            {
                result.Created++;
            }
            else
            {
                result.Updated++;
            }
        }

        // Profiles need every account: a client points to its employee, an employee to its administrator.
        var byLegacyId = accounts.ToDictionary(account => account.Row.Id);
        var defaultEmployee = state.DefaultEmployeeId;
        foreach (var (row, user, roles, created) in accounts)
        {
            if (roles.Contains(TenantRole.Client))
            {
                Client(context, state, row, user, byLegacyId, created);
            }

            if (roles.Contains(TenantRole.Employee))
            {
                defaultEmployee = Employee(context, state, row, user, byLegacyId, defaultEmployee);
            }

            Picture(context, state, row, user);
        }
    }

    /// <summary>The tenant roles of the legacy role names; SystemConfigurator is dropped (D-18), other unknown names are returned.</summary>
    public static IReadOnlyCollection<TenantRole> Roles(IReadOnlyCollection<string> names, out string[] unknown)
    {
        ArgumentNullException.ThrowIfNull(names);

        var roles = new HashSet<TenantRole>();
        var others = new List<string>();
        foreach (var name in names.Where(name => name != SystemConfiguratorRole))
        {
            if (TenantRoles.TryParse(name, out var role))
            {
                roles.Add(role);
            }
            else
            {
                others.Add(name);
            }
        }

        unknown = [.. others];
        return roles;
    }

    /// <summary>
    /// The personal data of a legacy user with the invalid optional values left out (one warning each). The legacy uses
    /// <c>FullName</c> as first name; an empty <c>Surname</c> takes the last word of <c>FullName</c>, or repeats it.
    /// </summary>
    public static (PersonDetails Details, IReadOnlyList<string> Dropped) Details(LegacyUser row, DateOnly? birthDate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);

        var first = row.FullName.Trim();
        var last = row.Surname.Trim();
        if (last.Length == 0)
        {
            var space = first.LastIndexOf(' ');
            (first, last) = space > 0 ? (first[..space].Trim(), first[(space + 1)..]) : (first, first);
        }

        var details = new PersonDetails(first, last, row.Email, birthDate, row.Phone, row.FiscalCode);
        var errors = Person.Validate(details, today);
        var dropped = OptionalFields.Where(errors.ContainsKey).ToArray();
        details = details with
        {
            Email = errors.ContainsKey("email") ? null : details.Email,
            Phone = errors.ContainsKey("phone") ? null : details.Phone,
            FiscalCode = errors.ContainsKey("fiscalCode") ? null : details.FiscalCode,
            BirthDate = errors.ContainsKey("birthDate") ? null : details.BirthDate,
        };
        return (details, dropped);
    }

    /// <summary>A user name the new rules accept: trimmed, no white space, at most <see cref="User.UserNameMaxLength"/>.</summary>
    public static string UserName(string legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        var name = string.Join('.', legacy.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length > User.UserNameMaxLength ? name[..User.UserNameMaxLength] : name;
    }

    private static (User User, bool Created)? Account(
        LegacyImportContext context, TenantState state, LegacyUser row, IReadOnlyCollection<TenantRole> roles, Dictionary<int, string> languages,
        bool assignedPassword, bool hashUnchanged)
    {
        var (details, dropped) = Details(row, context.LocalDate(row.DateOfBirth), context.Today);
        if (Person.Validate(details, context.Today).Count > 0)
        {
            context.Report.Skip(Table, row.Id, "first or last name not valid");
            return null;
        }

        var mappedUser = context.Ids.Find(Table, row.Id);
        var existing = mappedUser is { } mapped && state.Users.TryGetValue(mapped, out var found) ? found : null;
        if (Person.NormalizeFiscalCode(details.FiscalCode) is { } code && state.FiscalCodes.TryGetValue(code, out var holder) && holder != existing?.PersonId)
        {
            context.Report.Warn(Table, row.Id, "fiscal code already used by another person: left out");
            details = details with { FiscalCode = null };
        }

        foreach (var field in dropped)
        {
            context.Report.Warn(Table, row.Id, $"{field} not valid: left out");
        }

        var userName = UniqueUserName(context, state, row, existing);
        var email = row.Email.Contains('@', StringComparison.Ordinal) ? row.Email : null;
        var language = languages.GetValueOrDefault(row.LanguageId) ?? context.DefaultLanguage;
        var changedAt = LegacyImportContext.Instant(row.PasswordChangedAt ?? row.CreatedAt);

        if (existing is not null)
        {
            var person = state.People[existing.PersonId];
            person.Update(details, context.Today);
            existing.ChangeAccount(userName, email);
            existing.ChangeLanguage(language);
            existing.SetActive(row.IsActive);
            if (!existing.Roles.Order().SequenceEqual(roles.Order()))
            {
                existing.SetRoles(roles);
            }

            // A user who already signed in to the new system has a new hash: it wins over the legacy one.
            if (existing.PasswordFormat == PasswordFormat.LegacyBcrypt)
            {
                existing.ImportLegacyPassword(row.PasswordHash, changedAt, hashUnchanged ? existing.MustChangePassword : assignedPassword);
            }

            state.Track(person, existing);
            return (existing, false);
        }

        var personId = context.Ids.NewId();
        var created = Person.Create(personId, details, context.Today).Value;
        if (row.CustomFields is { Length: > 2 } customFields)
        {
            created.SetCustomFields(customFields);
        }

        var userId = context.Ids.NewId();
        var account = User.Create(userId, personId, userName, email, language, roles, row.IsActive);
        if (account.IsFailure)
        {
            context.Report.Skip(Table, row.Id, "the account is not valid");
            return null;
        }

        var user = account.Value;
        user.ImportLegacyPassword(row.PasswordHash, changedAt, assignedPassword);
        if (assignedPassword)
        {
            context.Report.Warn(Table, row.Id, "password assigned by the legacy application: to change at the next sign-in");
        }

        context.Tenant.Add(created);
        context.Tenant.Add(user);
        context.Ids.Add(Table, row.Id, userId);
        state.Track(created, user);
        return (user, true);
    }

    private static string UniqueUserName(LegacyImportContext context, TenantState state, LegacyUser row, User? existing)
    {
        var name = UserName(row.Username);
        if (name != row.Username)
        {
            context.Report.Warn(Table, row.Id, "user name with spaces: spaces replaced with dots");
        }

        if (name.Length == 0)
        {
            name = $"user.{row.Id}";
        }

        if (state.UserNames.TryGetValue(name, out var owner) && owner != existing?.Id)
        {
            context.Report.Warn(Table, row.Id, "user name already used: legacy id appended");
            name = $"{name}.{row.Id}";
        }

        return name;
    }

    private static void Client(
        LegacyImportContext context, TenantState state, LegacyUser row, User user,
        Dictionary<int, (LegacyUser Row, User User, IReadOnlyCollection<TenantRole> Roles, bool Created)> accounts, bool created)
    {
        Guid? employee = null;
        if (row.AssignedEmployeeId is { } legacyEmployee)
        {
            if (accounts.TryGetValue(legacyEmployee, out var assigned) && assigned.Roles.Contains(TenantRole.Employee))
            {
                employee = assigned.User.Id;
            }
            else
            {
                context.Report.Warn(Table, row.Id, "assigned employee not migrated or not an employee: client without employee");
            }
        }

        var since = LegacyImportContext.Instant(row.CreatedAt);
        if (state.Clients.TryGetValue(user.PersonId, out var profile))
        {
            if (employee is { } employeeId)
            {
                profile.Assign(employeeId, context.Now);
            }
            else
            {
                profile.Unassign(context.Now);
            }
        }
        else
        {
            profile = ClientProfile.Create(user.PersonId, employee, since);
            context.Tenant.Add(profile);
            state.Clients[user.PersonId] = profile;
        }

        if (!created)
        {
            return;
        }

        // D-23: migrated clients consent to marketing e-mails; the legacy privacy flag becomes a privacy consent.
        context.Tenant.Add(Consent.Record(
            context.Ids.NewId(), user.PersonId, ConsentPurpose.Marketing, ConsentChannel.Email, granted: true, ConsentSource.LegacyMigration,
            ConsentVersion, note: null, userId: null, since).Value);
        if (row.PrivacyConsent)
        {
            context.Tenant.Add(Consent.Record(
                context.Ids.NewId(), user.PersonId, ConsentPurpose.Privacy, ConsentChannel.Email, granted: true, ConsentSource.LegacyMigration,
                ConsentVersion, note: null, userId: null, since).Value);
        }
    }

    private static Guid? Employee(
        LegacyImportContext context, TenantState state, LegacyUser row, User user,
        Dictionary<int, (LegacyUser Row, User User, IReadOnlyCollection<TenantRole> Roles, bool Created)> accounts, Guid? defaultEmployee)
    {
        Guid? administrator = null;
        if (row.AssignedAdministratorId is { } legacyAdministrator)
        {
            if (accounts.TryGetValue(legacyAdministrator, out var assigned) && assigned.Roles.Contains(TenantRole.Administrator))
            {
                administrator = assigned.User.Id;
            }
            else
            {
                context.Report.Warn(Table, row.Id, "administrator not migrated or not an administrator: left out");
            }
        }

        var makeDefault = row.IsDefaultEmployee && (defaultEmployee is null || defaultEmployee == user.Id);
        if (row.IsDefaultEmployee && !makeDefault)
        {
            context.Report.Warn(Table, row.Id, "another employee is already the default employee (Q31)");
        }

        if (!state.Employees.TryGetValue(user.Id, out var profile))
        {
            if (administrator is null && !makeDefault)
            {
                // No row means not default and no administrator (EmployeeProfile).
                return defaultEmployee;
            }

            profile = EmployeeProfile.Create(user.Id);
            context.Tenant.Add(profile);
            state.Employees[user.Id] = profile;
        }

        profile.SetAdministrator(administrator);
        if (makeDefault)
        {
            profile.MakeDefault();
            return user.Id;
        }

        if (profile.ClearDefault() && defaultEmployee == user.Id)
        {
            return null;
        }

        return defaultEmployee;
    }

    private void Picture(LegacyImportContext context, TenantState state, LegacyUser row, User user)
    {
        if (string.IsNullOrEmpty(row.ProfileImage) || state.Images.Contains(user.Id))
        {
            return;
        }

        var comma = row.ProfileImage.IndexOf(',', StringComparison.Ordinal);
        var data = comma >= 0 ? row.ProfileImage[(comma + 1)..] : row.ProfileImage;
        var bytes = new byte[data.Length];
        var processed = Convert.TryFromBase64String(data, bytes, out var length)
            ? images.ResizeToJpeg(bytes.AsSpan(0, length), UserImage.MaxSide)
            : null;
        if (processed is null)
        {
            context.Report.Warn(Table, row.Id, "profile picture not readable: left out");
            return;
        }

        context.Tenant.Add(UserImage.Create(user.Id, processed.Content, processed.ContentType));
        state.Images.Add(user.Id);
    }

    /// <summary>Legacy users whose hash matches a password the legacy application assigned (mapping.md §2).</summary>
    private async Task<HashSet<int>> AssignedPasswordUsersAsync(IReadOnlyList<LegacyUser> rows, string? defaultPassword, CancellationToken cancellationToken)
    {
        string[] candidates = [.. AssignedPasswords.Append(defaultPassword).OfType<string>().Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)];
        var matches = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Parallel.ForEachAsync(rows, cancellationToken, (row, _) =>
        {
            if (candidates.Any(password => hasher.Verify(row.PasswordHash, PasswordFormat.LegacyBcrypt, password) != PasswordVerification.Failed))
            {
                matches.Add(row.Id);
            }

            return ValueTask.CompletedTask;
        });
        return [.. matches];
    }

    /// <summary>The records of the tenant the step reads or changes, loaded once.</summary>
    private sealed class TenantState
    {
        public Dictionary<Guid, Person> People { get; } = [];

        public Dictionary<Guid, User> Users { get; } = [];

        public Dictionary<Guid, ClientProfile> Clients { get; } = [];

        public Dictionary<Guid, EmployeeProfile> Employees { get; } = [];

        public HashSet<Guid> Images { get; } = [];

        /// <summary>User name (case-insensitive) → user id.</summary>
        public Dictionary<string, Guid> UserNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fiscal code → person id, among the people not deleted.</summary>
        public Dictionary<string, Guid> FiscalCodes { get; } = new(StringComparer.Ordinal);

        public Guid? DefaultEmployeeId => Employees.Values.FirstOrDefault(profile => profile.IsDefault)?.Id;

        public static async Task<TenantState> LoadAsync(LegacyImportContext context, CancellationToken cancellationToken)
        {
            var db = context.Tenant;
            var state = new TenantState();
            foreach (var person in await db.Set<Person>().ToListAsync(cancellationToken))
            {
                state.People[person.Id] = person;
                if (person.FiscalCode is { } code)
                {
                    state.FiscalCodes[code] = person.Id;
                }
            }

            foreach (var user in await db.Set<User>().ToListAsync(cancellationToken))
            {
                state.Users[user.Id] = user;
                state.UserNames[user.UserName] = user.Id;
            }

            foreach (var profile in await db.Set<ClientProfile>().ToListAsync(cancellationToken))
            {
                state.Clients[profile.Id] = profile;
            }

            foreach (var profile in await db.Set<EmployeeProfile>().ToListAsync(cancellationToken))
            {
                state.Employees[profile.Id] = profile;
            }

            state.Images.UnionWith(await db.Set<UserImage>().Select(image => image.Id).ToListAsync(cancellationToken));
            return state;
        }

        public void Track(Person person, User user)
        {
            People[person.Id] = person;
            Users[user.Id] = user;
            UserNames[user.UserName] = user.Id;
            if (person.FiscalCode is { } code)
            {
                FiscalCodes[code] = person.Id;
            }
        }
    }
}
