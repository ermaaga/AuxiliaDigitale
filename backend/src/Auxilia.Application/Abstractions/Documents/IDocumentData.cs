using Auxilia.Domain.Documents;

namespace Auxilia.Application.Abstractions.Documents;

/// <summary>
/// Which documents a caller may see (F10), applied in the query: everything, or what an employee may see — documents
/// of a case visible to the employee (D-04), documents without a case of a client assigned to the employee or whose
/// private specializations the employee all holds. Nothing when neither is set.
/// </summary>
public sealed record DocumentScope(bool Everything, Guid? EmployeeUserId)
{
    public static readonly DocumentScope None = new(false, null);
}

/// <summary>Sortable columns of the document lists (legacy: client, file name, year, area, uploaded by, uploaded at).</summary>
public enum DocumentSort
{
    UploadedAt,
    Client,
    FileName,
    ReferenceYear,
    Area,
    UploadedBy,
}

/// <summary>A page request of the document lists, already validated; text filters match anywhere, case-insensitive.</summary>
public sealed record DocumentFilter(
    DocumentScope Scope,
    Guid? ClientId,
    Guid? CaseId,
    Guid? FolderId,
    string? ClientName,
    string? FileName,
    string? Description,
    string? UploadedBy,
    int? ReferenceYear,
    Guid? AreaId,
    DocumentSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>A document with the names the lists show.</summary>
public sealed record DocumentRow(
    Document Document,
    string ClientName,
    string? CaseNumber,
    string? CaseServiceName,
    string? AreaName,
    string? UploadedByName);

/// <summary>What the F10 rules need about a client for documents without a case.</summary>
public sealed record DocumentClientAccess(bool AssignedToEmployee, bool HasPrivateSpecializationOutsideEmployee);

public sealed record DocumentAreaRow(DocumentArea Area, int DocumentCount);

/// <summary>Documents and areas of the current tenant (F14), one unit of work (joins the operation's transaction).</summary>
public interface IDocumentData : IAsyncDisposable
{
    Task<(IReadOnlyList<DocumentRow> Items, int Total)> PageAsync(DocumentFilter filter, CancellationToken cancellationToken);

    /// <summary>The document with its names (untracked); <c>null</c> when missing or its client is deleted.</summary>
    Task<DocumentRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The document (tracked).</summary>
    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The documents of a case (untracked), for the ZIP.</summary>
    Task<IReadOnlyList<Document>> OfCaseAsync(Guid caseId, CancellationToken cancellationToken);

    Task<DocumentClientAccess> ClientAccessAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken);

    /// <summary>The names among <paramref name="fileNames"/> another document of the same client, case and folder has (case-insensitive).</summary>
    Task<IReadOnlySet<string>> NamesTakenAsync(
        Guid clientId, Guid? caseId, Guid? folderId, IReadOnlyCollection<string> fileNames, Guid? exceptId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentAreaRow>> AreasAsync(CancellationToken cancellationToken);

    /// <summary>The area (tracked).</summary>
    Task<DocumentArea?> FindAreaAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> AreaNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    void Add(Document document);

    void Remove(Document document);

    void Add(DocumentArea area);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDocumentDataFactory
{
    Task<IDocumentData> OpenAsync(CancellationToken cancellationToken);
}
