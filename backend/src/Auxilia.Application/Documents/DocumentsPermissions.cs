using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Documents;

/// <summary>Permissions of the documents module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class DocumentsPermissions
{
    /// <summary>Client folders and files (F14).</summary>
    public const string ViewDocuments = "documents.files.view";

    /// <summary>Upload, rename and delete files (F14).</summary>
    public const string ManageDocuments = "documents.files.manage";

    /// <summary>The list of document areas (F14: legacy free text, now managed).</summary>
    public const string ManageAreas = "documents.areas.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewDocuments, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageDocuments, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageAreas, [TenantRole.Administrator]),
    ];
}
