using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Mailbox;

/// <summary>Connected accounts (Google, Microsoft, PEC / IMAP), their mail and external calendars (N05, ADR 0021).</summary>
public sealed class MailboxModule : IModuleDescriptor
{
    public const string ModuleCode = "mailbox";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 30000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = MailboxPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = MailboxSettings.All;

    /// <summary>No page yet: the accounts live in the profile (C-03); the mail page comes with C-07.</summary>
    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<ConnectedAccountPolicy>();
        services.TryAddScoped<IConnectedAccountManager, ConnectedAccountManager>();
        services.TryAddScoped<IConnectedAccountQueryService, ConnectedAccountQueryService>();
        services.TryAddScoped<IAccountSyncRequester, AccountSyncRequester>();
        services.TryAddScoped<MailboxAccessResolver>();
        services.TryAddScoped<IAccountSyncService, AccountSyncService>();
        services.TryAddScoped<IMailboxSyncQueryService, MailboxSyncQueryService>();
        services.TryAddScoped<LiveMessages>();
        services.TryAddScoped<IMailMessageQueryService, MailMessageQueryService>();
        services.TryAddScoped<IMailMessageManager, MailMessageManager>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScopeSynchronizer, CalendarSynchronizer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScopeSynchronizer, MailSynchronizer>());
    }
}
