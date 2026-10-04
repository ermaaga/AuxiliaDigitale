using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Imports;
using Auxilia.Contracts.Directory;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Directory;

/// <summary>
/// Clients from a workbook (F19, Q47): the staff form rules (person, e-mail = user name, fiscal code, birth date),
/// fiscal code and user name unique in the file and in the tenant, the employee in charge by user name (else the
/// default employee, Q31). Created by <see cref="IClientManager"/>: sign-in enabled, activation e-mail (D-06). Optional
/// tags by name (separated by commas or semicolons) and the e-mail marketing consent (yes/no, source Import, N01).
/// </summary>
internal sealed class ClientImportTarget(
    IClientManager clients, IImportLookups lookups, IConsentTagDataFactory tags, ITagManager tagManager, ConsentManager consents, TimeProvider clock)
    : IImportTarget
{
    public string Entity => "Client";

    public IReadOnlyList<ImportField> Fields { get; } =
    [
        new("firstName", "FirstName", Required: true),
        new("lastName", "LastName", Required: true),
        new("email", "Email", Required: true),
        new("fiscalCode", "FiscalCode", Required: true),
        new("birthDate", "BirthDate", Required: true),
        new("phone", "Phone", Required: false),
        new("employee", "app.imports.field.employee", Required: false),
        new("tags", "app.imports.field.tags", Required: false),
        new("marketingEmailConsent", "app.imports.field.marketingEmailConsent", Required: false),
    ];

    public async Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var errors = rows.Select(_ => new ImportRowErrors()).ToArray();
        var takenCodes = await lookups.TakenFiscalCodesAsync(Values(rows, "fiscalCode", Person.NormalizeFiscalCode), cancellationToken);
        var takenNames = await lookups.TakenUserNamesAsync(Values(rows, "email"), cancellationToken);
        var employees = await lookups.EmployeesAsync(Values(rows, "employee"), cancellationToken);
        var tagIds = await TagIdsAsync([.. rows.SelectMany(TagNames)], cancellationToken);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowErrors = errors[index];
            ImportValues.CheckRequired(row, Fields, rowErrors);
            ImportValues.Boolean(row, "marketingEmailConsent", rowErrors);
            if (TagNames(row).Any(name => !tagIds.ContainsKey(name)))
            {
                rowErrors.Add("tags", ImportValues.NotFound);
            }

            var details = Details(row, rowErrors);
            if (Person.Create(Guid.Empty, details, today) is { IsFailure: true } person)
            {
                rowErrors.Add(person.Error!);
            }

            if (Person.NormalizeFiscalCode(row.Value("fiscalCode")) is { } code && takenCodes.Contains(code))
            {
                rowErrors.Add("fiscalCode", ImportValues.Taken);
            }

            if (row.Value("email") is { } email && takenNames.Contains(email))
            {
                rowErrors.Add("email", ImportValues.Taken);
            }

            if (row.Value("employee") is { } employee && !employees.ContainsKey(employee))
            {
                rowErrors.Add("employee", ImportValues.NotFound);
            }
        }

        ImportValues.CheckDuplicates(rows, errors, "fiscalCode", Person.NormalizeFiscalCode);
        ImportValues.CheckDuplicates(rows, errors, "email");
        return errors;
    }

    public async Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var details = Details(row, new ImportRowErrors());
        Guid? employee = null;
        if (row.Value("employee") is { } userName)
        {
            var found = await lookups.EmployeesAsync([userName], cancellationToken);
            employee = found.TryGetValue(userName, out var id) ? id : null;
        }

        var created = await clients.CreateAsync(
            new CreateClientRequest(details.FirstName!, details.LastName!, details.BirthDate, details.Email!, details.Phone, details.FiscalCode!, null, employee),
            cancellationToken);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error!);
        }

        var clientId = created.Value.Id;
        var names = TagNames(row).ToArray();
        if (names.Length > 0)
        {
            var ids = await TagIdsAsync(names, cancellationToken);
            var tagged = await tagManager.SetClientTagsAsync(clientId, [.. names.Select(name => ids.GetValueOrDefault(name)).Where(id => id != Guid.Empty)], cancellationToken);
            if (tagged.IsFailure)
            {
                return Result.Failure<Guid>(tagged.Error!);
            }
        }

        if (ImportValues.Boolean(row, "marketingEmailConsent", new ImportRowErrors()) is { } granted)
        {
            var recorded = await consents.RecordAsync(clientId, ConsentPurpose.Marketing, ConsentChannel.Email, granted, ConsentSource.Import, null, null, cancellationToken);
            if (recorded.IsFailure)
            {
                return Result.Failure<Guid>(recorded.Error!);
            }
        }

        return clientId;
    }

    private static IEnumerable<string> TagNames(ImportRow row) =>
        (row.Value("tags") ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<IReadOnlyDictionary<string, Guid>> TagIdsAsync(string[] names, CancellationToken cancellationToken)
    {
        if (names.Length == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        }

        await using var store = await tags.OpenAsync(cancellationToken);
        return await store.TagIdsByNameAsync(names, cancellationToken);
    }

    private static PersonDetails Details(ImportRow row, ImportRowErrors errors) =>
        ClientRules.Details(
            row.Value("firstName"), row.Value("lastName"), row.Value("email"), ImportValues.Date(row, "birthDate", errors), row.Value("phone"), row.Value("fiscalCode"));

    internal static string[] Values(IReadOnlyList<ImportRow> rows, string key, Func<string, string?>? normalize = null) =>
        [.. rows.Select(row => row.Value(key) is { } value ? (normalize is null ? value : normalize(value)) : null).OfType<string>()];
}

/// <summary>
/// Employees from a workbook (F19, Q47): the staff form rules, e-mail = user name unique, fiscal code unique when
/// given; <c>canSignIn</c> (default yes) sends the activation e-mail (D-06). Created by <see cref="IEmployeeManager"/>.
/// </summary>
internal sealed class EmployeeImportTarget(IEmployeeManager employees, IImportLookups lookups, TimeProvider clock) : IImportTarget
{
    public string Entity => "Employee";

    public IReadOnlyList<ImportField> Fields { get; } =
    [
        new("firstName", "FirstName", Required: true),
        new("lastName", "LastName", Required: true),
        new("email", "Email", Required: true),
        new("fiscalCode", "FiscalCode", Required: false),
        new("birthDate", "BirthDate", Required: true),
        new("phone", "Phone", Required: false),
        new("canSignIn", "app.imports.field.canSignIn", Required: false),
    ];

    public async Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var errors = rows.Select(_ => new ImportRowErrors()).ToArray();
        var takenCodes = await lookups.TakenFiscalCodesAsync(ClientImportTarget.Values(rows, "fiscalCode", Person.NormalizeFiscalCode), cancellationToken);
        var takenNames = await lookups.TakenUserNamesAsync(ClientImportTarget.Values(rows, "email"), cancellationToken);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowErrors = errors[index];
            ImportValues.CheckRequired(row, Fields, rowErrors);
            ImportValues.Boolean(row, "canSignIn", rowErrors);
            var details = Details(row, rowErrors);
            var person = Person.Create(Guid.Empty, details, today);
            var checkedPerson = EmployeeRules.Check(person.IsFailure ? Result.Failure(person.Error!) : Result.Success(), details);
            if (checkedPerson.IsFailure)
            {
                rowErrors.Add(checkedPerson.Error!);
            }

            if (Person.NormalizeFiscalCode(row.Value("fiscalCode")) is { } code && takenCodes.Contains(code))
            {
                rowErrors.Add("fiscalCode", ImportValues.Taken);
            }

            if (row.Value("email") is { } email && takenNames.Contains(email))
            {
                rowErrors.Add("email", ImportValues.Taken);
            }
        }

        ImportValues.CheckDuplicates(rows, errors, "fiscalCode", Person.NormalizeFiscalCode);
        ImportValues.CheckDuplicates(rows, errors, "email");
        return errors;
    }

    public async Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var errors = new ImportRowErrors();
        var details = Details(row, errors);
        var created = await employees.CreateAsync(
            new CreateEmployeeRequest(
                details.FirstName!, details.LastName!, details.BirthDate, details.Email!, details.Phone, details.FiscalCode,
                ImportValues.Boolean(row, "canSignIn", errors) ?? true),
            cancellationToken);
        return created.IsFailure ? Result.Failure<Guid>(created.Error!) : created.Value.Id;
    }

    private static PersonDetails Details(ImportRow row, ImportRowErrors errors) =>
        EmployeeRules.Details(
            row.Value("firstName"), row.Value("lastName"), row.Value("email"), ImportValues.Date(row, "birthDate", errors), row.Value("phone"), row.Value("fiscalCode"));
}
