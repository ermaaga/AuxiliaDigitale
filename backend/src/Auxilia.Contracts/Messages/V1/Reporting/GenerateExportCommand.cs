namespace Auxilia.Contracts.Messages.V1.Reporting;

/// <summary>
/// Writes a large export (F26) as the user who asked for it: <paramref name="Roles"/> are the user's roles at request
/// time (the Worker checks permissions and F10 with them).
/// </summary>
public sealed record GenerateExportCommand(Guid ExportId, IReadOnlyList<string> Roles) : ITenantMessage;
