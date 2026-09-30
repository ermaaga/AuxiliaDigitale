using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Messaging;

/// <summary>Outbound channels, sending accounts, sender rules, templates (N03). Core: transactional e-mails need it.</summary>
public sealed class MessagingModule : IModuleDescriptor
{
    public const string ModuleCode = "messaging";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 25000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
    }
}
