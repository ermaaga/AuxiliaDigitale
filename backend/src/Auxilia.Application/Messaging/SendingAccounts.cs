using System.ComponentModel;

using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Messaging;

namespace Auxilia.Application.Messaging;

/// <summary>Accounts and rules of a tenant without secrets, cached as <c>t:{slug}:messaging:accounts:current</c>.</summary>
[ImmutableObject(true)]
public sealed record MessagingSnapshot(IReadOnlyList<AccountInfo> Accounts, IReadOnlyList<RuleInfo> Rules);

public sealed record AccountInfo(Guid Id, MessageChannel Channel, string Provider, bool IsDefault, bool IsActive);

public sealed record RuleInfo(MessageChannel Channel, MessagePurpose Purpose, string? Role, Guid AccountId, int Priority);

internal sealed class MessagingSnapshotCache : ReferenceDataCache<MessagingSnapshot>
{
    public const string ModuleName = "messaging";

    private readonly IMessagingDataFactory data;

    public MessagingSnapshotCache(IReferenceDataCache cache, ITenantContext tenantContext, IMessagingDataFactory data)
        : base(cache, tenantContext) => this.data = data;

    protected override string Module => ModuleName;

    protected override string Entity => "accounts";

    protected override async Task<MessagingSnapshot> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return new MessagingSnapshot([], []);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var accounts = await store.AccountsAsync(cancellationToken);
        var rules = await store.RulesAsync(null, cancellationToken);
        return new MessagingSnapshot(
            accounts.Select(account => new AccountInfo(account.Id, account.Channel, account.Provider, account.IsDefault, account.IsActive)).ToArray(),
            rules.Select(rule => new RuleInfo(rule.Channel, rule.Purpose, rule.Role, rule.AccountId, rule.Priority)).ToArray());
    }
}

/// <summary>
/// N03 / ARCHITECTURE §8.1: (channel, purpose, role of the sender) → (channel, purpose, any role) → default account of
/// the channel. Only active accounts; among matching rules the lowest priority wins. A sender with several roles
/// matches the rules of any of them. Messages without a user (system) use no role.
/// </summary>
internal static class SendingAccountResolution
{
    public static AccountInfo? Resolve(MessagingSnapshot snapshot, MessageChannel channel, MessagePurpose purpose, IReadOnlyCollection<string> roles)
    {
        var active = snapshot.Accounts.Where(account => account.IsActive && account.Channel == channel).ToDictionary(account => account.Id);
        var candidates = snapshot.Rules
            .Where(rule => rule.Channel == channel && rule.Purpose == purpose && active.ContainsKey(rule.AccountId))
            .ToArray();

        var byRole = candidates.Where(rule => rule.Role is not null && roles.Contains(rule.Role)).OrderBy(rule => rule.Priority).FirstOrDefault();
        var anyRole = candidates.Where(rule => rule.Role is null).OrderBy(rule => rule.Priority).FirstOrDefault();
        var rule = byRole ?? anyRole;

        return rule is not null ? active[rule.AccountId] : active.Values.FirstOrDefault(account => account.IsDefault);
    }
}
