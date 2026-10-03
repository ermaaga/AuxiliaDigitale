namespace Auxilia.Contracts.Messages.V1.Documents;

/// <summary>Checks a document just uploaded (stored file against its checksum) and tells the uploader (F14).</summary>
public sealed record ProcessDocumentCommand(Guid DocumentId) : ITenantMessage;
