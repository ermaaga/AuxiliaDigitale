using Auxilia.Domain.Imports;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Imports;

/// <summary>A column of an import (F19): the header of the template is <paramref name="Key"/>, its label a translation key.</summary>
public sealed record ImportField(string Key, string LabelKey, bool Required);

/// <summary>A row read from the workbook: worksheet row number and the text of each known column (empty cells missing).</summary>
public sealed record ImportRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string? Value(string key) => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Field → translation keys of the errors of one row; empty when the row is valid.</summary>
public sealed class ImportRowErrors
{
    private readonly Dictionary<string, string[]> errors = new(StringComparer.Ordinal);

    public bool IsValid => errors.Count == 0;

    public IReadOnlyDictionary<string, string[]> Errors => errors;

    public void Add(string field, string messageKey) =>
        errors[field] = errors.TryGetValue(field, out var existing) ? [.. existing, messageKey] : [messageKey];

    /// <summary>The field errors of a failed Manager call (validation errors, else the error code).</summary>
    public void Add(SharedKernel.Results.Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.ValidationErrors.Count > 0)
        {
            foreach (var (field, keys) in error.ValidationErrors)
            {
                foreach (var key in keys)
                {
                    Add(field, key);
                }
            }

            return;
        }

        Add("row", "errors." + error.DisplayCode);
    }
}

/// <summary>
/// What a module lets the System import (F19, D-18), registered by the module that owns the entity. Validation sees the
/// whole file (duplicates inside it, references by natural keys); import creates one record through the module's own
/// Manager (rules, activation per D-06) in an operation of its own.
/// </summary>
public interface IImportTarget
{
    /// <summary><c>Employee</c>, <c>Client</c>, <c>Service</c> or <c>Case</c>.</summary>
    string Entity { get; }

    IReadOnlyList<ImportField> Fields { get; }

    /// <returns>One entry per row, in order.</returns>
    Task<IReadOnlyList<ImportRowErrors>> ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken);

    /// <returns>The id of the record created.</returns>
    Task<Result<Guid>> ImportAsync(ImportRow row, CancellationToken cancellationToken);
}

/// <summary>The rows of the first worksheet, or why the file cannot be used.</summary>
public sealed record ImportSheet(IReadOnlyList<ImportRow> Rows, IReadOnlyList<string> MissingColumns);

/// <summary>Excel workbooks of the imports (adapter: ClosedXML).</summary>
public interface IImportWorkbook
{
    /// <summary>Row 1 = the field keys (case-insensitive, unknown columns ignored), then one record per non-empty row; dates as <c>yyyy-MM-dd</c>, numbers invariant.</summary>
    /// <returns><c>null</c> when the content is not an Excel workbook.</returns>
    ImportSheet? Read(byte[] content, IReadOnlyList<ImportField> fields);

    /// <summary>The template: one header row, required columns red, the others light grey.</summary>
    byte[] Template(string sheetName, IReadOnlyList<ImportField> fields);
}

public sealed record ImportTypeRow(Guid Id, string Name, string TargetEntity, DateTimeOffset CreatedAt, int ImportCount);

public sealed record ImportJobListRow(
    Guid Id, Guid ImportTypeId, string ImportTypeName, string TargetEntity, string Name, string FileName, ImportJobStatus Status,
    int TotalRows, int ProcessedRows, int SuccessRows, int FailedRows, string? ErrorCode, string? ErrorMessage,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);

public sealed record ImportJobRowData(int RowNumber, ImportRowStatus Status, string Data, string? Errors, Guid? EntityId);

/// <summary>Imports of the current tenant (F19), one unit of work.</summary>
public interface IImportData : IAsyncDisposable
{
    Task<IReadOnlyList<ImportTypeRow>> TypesAsync(CancellationToken cancellationToken);

    Task<ImportType?> FindTypeAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> TypeHasJobsAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ImportJobListRow> Items, int Total)> JobsAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<ImportJobListRow?> JobAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<ImportJob?> FindJobAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ImportJobRowData> Items, int Total)> RowsAsync(Guid jobId, ImportRowStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Tracked, from <paramref name="afterRow"/> on, in row order.</summary>
    Task<IReadOnlyList<ImportJobRow>> ValidRowsAsync(Guid jobId, int afterRow, int take, CancellationToken cancellationToken);

    Task DeleteRowsAsync(Guid jobId, CancellationToken cancellationToken);

    void Add(ImportType type);

    void Remove(ImportType type);

    void Add(ImportJob job);

    void Remove(ImportJob job);

    void AddRows(IEnumerable<ImportJobRow> rows);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IImportDataFactory
{
    Task<IImportData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Lookups by natural key for the validation of imports (F19), one query per kind for the whole file. Keys compare
/// case-insensitively; the dictionaries use <see cref="StringComparer.OrdinalIgnoreCase"/>.
/// </summary>
public interface IImportLookups
{
    /// <summary>Fiscal codes (normalised, upper case) already used by a person.</summary>
    Task<IReadOnlySet<string>> TakenFiscalCodesAsync(IReadOnlyCollection<string> fiscalCodes, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> TakenUserNamesAsync(IReadOnlyCollection<string> userNames, CancellationToken cancellationToken);

    /// <summary>User name → user id of active users with the Employee role.</summary>
    Task<IReadOnlyDictionary<string, Guid>> EmployeesAsync(IReadOnlyCollection<string> userNames, CancellationToken cancellationToken);

    /// <summary>Fiscal code → client id (clients not deleted).</summary>
    Task<IReadOnlyDictionary<string, Guid>> ClientsAsync(IReadOnlyCollection<string> fiscalCodes, CancellationToken cancellationToken);

    /// <summary>Name → id of the services (not deleted); <paramref name="activeOnly"/> for the references of new cases.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ServicesAsync(IReadOnlyCollection<string> names, bool activeOnly, CancellationToken cancellationToken);

    /// <summary>Name → id of the active service categories.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ServiceCategoriesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    /// <summary>Name → id of the active Employee specializations.</summary>
    Task<IReadOnlyDictionary<string, Guid>> EmployeeSpecializationsAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);
}

/// <summary>Parsing of import cells (dates <c>yyyy-MM-dd</c> or <c>dd/MM/yyyy</c>, numbers with dot or comma) and the common row checks.</summary>
public static class ImportValues
{
    public const string Required = "validation.imports.required";
    public const string InvalidDate = "validation.imports.date";
    public const string InvalidNumber = "validation.imports.number";
    public const string InvalidBoolean = "validation.imports.boolean";
    public const string Duplicate = "validation.imports.duplicate";
    public const string NotFound = "validation.imports.notFound";
    public const string Taken = "validation.imports.taken";

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd.MM.yyyy"];

    /// <summary>A required field without value gets <see cref="Required"/>.</summary>
    public static void CheckRequired(ImportRow row, IReadOnlyList<ImportField> fields, ImportRowErrors errors)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var field in fields.Where(field => field.Required && row.Value(field.Key) is null))
        {
            errors.Add(field.Key, Required);
        }
    }

    /// <returns>The date, <c>null</c> when empty; an error is added when it cannot be read.</returns>
    public static DateOnly? Date(ImportRow row, string key, ImportRowErrors errors)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(errors);
        if (row.Value(key) is not { } text)
        {
            return null;
        }

        var datePart = text.Split('T', ' ')[0];
        if (DateOnly.TryParseExact(datePart, DateFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(key, InvalidDate);
        return null;
    }

    public static decimal? Amount(ImportRow row, string key, ImportRowErrors errors)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(errors);
        if (row.Value(key) is not { } text)
        {
            return null;
        }

        // "1.234,56" (Italian) or "1234.56" (invariant, Excel numbers).
        var normalized = text.Contains(',', StringComparison.Ordinal) ? text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.') : text;
        if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add(key, InvalidNumber);
        return null;
    }

    public static int? WholeNumber(ImportRow row, string key, ImportRowErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var value = Amount(row, key, errors);
        if (value is null)
        {
            return null;
        }

        if (value != decimal.Truncate(value.Value) || value is > int.MaxValue or < int.MinValue)
        {
            errors.Add(key, InvalidNumber);
            return null;
        }

        return (int)value.Value;
    }

    public static bool? Boolean(ImportRow row, string key, ImportRowErrors errors)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(errors);
        switch (row.Value(key)?.ToUpperInvariant())
        {
            case null:
                return null;
            case "TRUE" or "1" or "YES" or "Y" or "SI" or "SÌ" or "VERO" or "X":
                return true;
            case "FALSE" or "0" or "NO" or "N" or "FALSO":
                return false;
            default:
                errors.Add(key, InvalidBoolean);
                return null;
        }
    }

    /// <summary>Adds <see cref="Duplicate"/> to every row whose value of <paramref name="key"/> (case-insensitive) appears in an earlier row.</summary>
    public static void CheckDuplicates(IReadOnlyList<ImportRow> rows, IReadOnlyList<ImportRowErrors> errors, string key, Func<string, string?>? normalize = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(errors);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows.Count; index++)
        {
            var value = rows[index].Value(key) is { } text ? (normalize is null ? text : normalize(text)) : null;
            if (value is not null && !seen.Add(value))
            {
                errors[index].Add(key, Duplicate);
            }
        }
    }
}
