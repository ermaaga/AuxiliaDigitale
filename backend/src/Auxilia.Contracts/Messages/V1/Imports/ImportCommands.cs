namespace Auxilia.Contracts.Messages.V1.Imports;

/// <summary>Reads and validates the rows of an uploaded import file (F19).</summary>
public sealed record ValidateImportCommand(Guid ImportId) : ITenantMessage;

/// <summary>Imports the valid rows of a confirmed import (F19).</summary>
public sealed record ProcessImportCommand(Guid ImportId) : ITenantMessage;
