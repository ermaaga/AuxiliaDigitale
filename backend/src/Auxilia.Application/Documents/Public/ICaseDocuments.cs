using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Documents.Public;

/// <summary>A file filed by another module: its content (read once) and the name it gets.</summary>
public sealed record CaseFile(Stream Content, string FileName);

/// <summary>Files filed into a case, all in the same folder and with the same description.</summary>
public sealed record CaseFiling(Guid CaseId, Guid? FolderId, string? Description, IReadOnlyList<CaseFile> Files);

/// <summary>
/// Documents filed into a case by another module (Mailbox "file into case", C-06b): the same checks as an upload
/// (type whitelist and content, size, F10 rules of the case for the caller, the folder of the case's service), the
/// client is the case's, a name already used in the folder gets <c>" (2)"</c>…; the Worker checks the stored files.
/// </summary>
public interface ICaseDocuments
{
    /// <returns>The ids of the documents, in the order of the files.</returns>
    Task<Result<IReadOnlyList<Guid>>> FileAsync(CaseFiling filing, CancellationToken cancellationToken);
}
