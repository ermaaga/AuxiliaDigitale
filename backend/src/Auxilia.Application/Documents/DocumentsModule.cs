using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Documents;

/// <summary>Documents, areas, storage (F14, F33).</summary>
public sealed class DocumentsModule : IModuleDescriptor
{
    public const string ModuleCode = "documents";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 16000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = DocumentsPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = DocumentsSettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("documents", "/documents", "folder", 60, [TenantRole.Administrator, TenantRole.Employee], DocumentsPermissions.ViewDocuments),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("document")];

    public void AddServices(IServiceCollection services)
    {
    }
}
