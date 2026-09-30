using System.Text.Json;

using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Messaging.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Messaging;

/// <summary>
/// Sending accounts and sender rules of the current tenant (N03), managed by the System from the console (S-03;
/// authorization with P2-06). Secrets are protected before they are stored and never returned. Every change evicts the
/// tenant's messaging snapshot after the commit.
/// </summary>
public interface IMessagingAccountManager
{
    /// <summary>The first account of a channel becomes its default.</summary>
    Task<Result<Guid>> CreateAccountAsync(CreateMessagingAccount request, CancellationToken cancellationToken);

    /// <param name="request">A null <see cref="UpdateMessagingAccount.Secret"/> keeps the stored secret.</param>
    Task<Result> UpdateAccountAsync(UpdateMessagingAccount request, CancellationToken cancellationToken);

    /// <summary>Makes the account the default of its channel (the previous default stops being it).</summary>
    Task<Result> SetDefaultAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<Result> SetAccountActiveAsync(Guid accountId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Replaces the rules of a channel.</summary>
    Task<Result> SetSenderRulesAsync(MessageChannel channel, IReadOnlyList<SenderRuleInput> rules, CancellationToken cancellationToken);

    /// <summary>Sends a test message through the account right away (not queued) and records it in the outbound log.</summary>
    Task<Result> SendTestAsync(Guid accountId, string recipient, string language, CancellationToken cancellationToken);
}

/// <param name="Settings">Non-secret provider settings (JSON object).</param>
/// <param name="Secret">Plain secret (password, token); protected before storage.</param>
public sealed record CreateMessagingAccount(MessageChannel Channel, string Provider, string Name, JsonElement Settings, string? Secret);

public sealed record UpdateMessagingAccount(Guid AccountId, string Name, JsonElement Settings, string? Secret);

/// <param name="Role">A tenant role name, or null for any role.</param>
public sealed record SenderRuleInput(MessagePurpose Purpose, string? Role, Guid AccountId, int Priority);

internal sealed class MessagingAccountManager : IMessagingAccountManager
{
    private readonly IOperationRunner operations;
    private readonly IMessagingDataFactory data;
    private readonly IEnumerable<IMessageChannel> channels;
    private readonly IAccountSecretProtector secrets;
    private readonly ITemplateRenderer templates;
    private readonly ITenantContext tenantContext;
    private readonly IReferenceDataCache cache;
    private readonly TimeProvider timeProvider;

    public MessagingAccountManager(
        IOperationRunner operations,
        IMessagingDataFactory data,
        IEnumerable<IMessageChannel> channels,
        IAccountSecretProtector secrets,
        ITemplateRenderer templates,
        ITenantContext tenantContext,
        IReferenceDataCache cache,
        TimeProvider timeProvider)
    {
        this.operations = operations;
        this.data = data;
        this.channels = channels;
        this.secrets = secrets;
        this.templates = templates;
        this.tenantContext = tenantContext;
        this.cache = cache;
        this.timeProvider = timeProvider;
    }

    public Task<Result<Guid>> CreateAccountAsync(CreateMessagingAccount request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Messaging.CreateAccount, new { request.Channel, request.Provider }, async scope =>
        {
            var settings = SettingsJson(request.Settings);
            if (Channel(request.Provider) is { } channel && (channel.Channel != request.Channel || !channel.AreSettingsValid(settings)))
            {
                return Errors.Messaging.AccountSettingsInvalid("settings");
            }

            var created = MessagingAccount.Create(Guid.CreateVersion7(), request.Channel, request.Provider, request.Name, settings, Protect(request.Secret));
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var account = created.Value;
            if (!(await store.AccountsAsync(cancellationToken)).Any(item => item.Channel == account.Channel && item.IsDefault))
            {
                account.SetDefault(true);
            }

            store.Add(account);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("MessagingAccount", account.Id);
            InvalidateAfterCommit(scope);
            return Result.Success(account.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAccountAsync(UpdateMessagingAccount request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return WithAccountAsync(Operations.Messaging.UpdateAccount, request.AccountId, (account, _) =>
        {
            var settings = SettingsJson(request.Settings);
            if (Channel(account.Provider) is { } channel && !channel.AreSettingsValid(settings))
            {
                return Task.FromResult<Result>(Errors.Messaging.AccountSettingsInvalid("settings"));
            }

            return Task.FromResult(account.Update(request.Name, settings, Protect(request.Secret)));
        }, cancellationToken);
    }

    public Task<Result> SetDefaultAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        WithAccountAsync(Operations.Messaging.SetDefaultAccount, accountId, async (account, store) =>
        {
            var set = account.SetDefault(true);
            if (set.IsFailure)
            {
                return set;
            }

            foreach (var other in (await store.AccountsAsync(cancellationToken)).Where(item => item.Channel == account.Channel && item.Id != account.Id && item.IsDefault))
            {
                other.SetDefault(false);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetAccountActiveAsync(Guid accountId, bool isActive, CancellationToken cancellationToken) =>
        WithAccountAsync(Operations.Messaging.SetAccountActive, accountId, (account, _) => Task.FromResult(account.SetActive(isActive)), cancellationToken);

    public Task<Result> SetSenderRulesAsync(MessageChannel channel, IReadOnlyList<SenderRuleInput> rules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return operations.RunAsync(Operations.Messaging.SetSenderRules, new { Channel = channel, RuleCount = rules.Count }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var accounts = (await store.AccountsAsync(cancellationToken)).ToDictionary(account => account.Id);
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                var validRole = rule.Role is null || Enum.TryParse<TenantRole>(rule.Role, ignoreCase: false, out _);
                if (!validRole || !accounts.TryGetValue(rule.AccountId, out var account) || account.Channel != channel || !account.IsActive)
                {
                    return Errors.Messaging.SenderRuleInvalid(index);
                }
            }

            foreach (var existing in await store.RulesAsync(channel, cancellationToken))
            {
                store.Remove(existing);
            }

            foreach (var rule in rules)
            {
                store.Add(new SenderRule(Guid.CreateVersion7(), channel, rule.Purpose, rule.Role, rule.AccountId, rule.Priority));
            }

            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> SendTestAsync(Guid accountId, string recipient, string language, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        return operations.RunAsync(Operations.Messaging.SendTestMessage, new { MessagingAccountId = accountId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAccountAsync(accountId, cancellationToken) is not { } account)
            {
                return Errors.Messaging.AccountNotFound();
            }

            if (Channel(account.Provider) is not { } channel)
            {
                return Errors.Messaging.ChannelNotAvailable(account.Provider);
            }

            if (!channel.IsValidRecipient(recipient))
            {
                return Errors.Messaging.RecipientInvalid();
            }

            var content = await MessageContent.RenderAsync(
                store, templates, account.Channel, MessageTemplates.AccountTest, language, tenantContext.Tenant.DefaultLanguage,
                new Dictionary<string, object?> { ["accountName"] = account.Name, ["tenantName"] = tenantContext.Tenant.Slug }, cancellationToken);
            if (content.IsFailure)
            {
                return content;
            }

            var message = new OutboundMessage(
                Guid.CreateVersion7(), account.Channel, MessagePurpose.Transactional, account.Id, recipient, MessageTemplates.AccountTest,
                content.Value.Language, content.Value.Subject, content.Value.Body, timeProvider.GetUtcNow());
            scope.SetEntity("OutboundMessage", message.Id);

            var outcome = await DeliverySteps.SendAsync(channel, account, message, secrets, cancellationToken);
            if (outcome.IsSuccess)
            {
                message.MarkSent(timeProvider.GetUtcNow());
            }
            else
            {
                message.MarkFailed(outcome.Error!.DisplayCode, timeProvider.GetUtcNow());
            }

            store.Add(message);
            await store.SaveChangesAsync(cancellationToken);
            return outcome;
        }, cancellationToken);
    }

    private Task<Result> WithAccountAsync(
        Diagnostics.OperationDescriptor operation, Guid accountId, Func<MessagingAccount, IMessagingData, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { MessagingAccountId = accountId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAccountAsync(accountId, cancellationToken) is not { } account)
            {
                return Errors.Messaging.AccountNotFound();
            }

            var result = await change(account, store);
            if (result.IsFailure)
            {
                return result;
            }

            await store.SaveChangesAsync(cancellationToken);
            InvalidateAfterCommit(scope);
            return Result.Success();
        }, cancellationToken);

    private void InvalidateAfterCommit(IOperationScope scope)
    {
        var slug = tenantContext.Tenant.Slug;
        scope.OnCommitted(ct => cache.InvalidateAsync(CacheTags.Tenant(slug, MessagingSnapshotCache.ModuleName), ct));
    }

    private IMessageChannel? Channel(string provider) =>
        channels.FirstOrDefault(channel => string.Equals(channel.Provider, provider, StringComparison.Ordinal));

    private string? Protect(string? secret) => string.IsNullOrEmpty(secret) ? null : secrets.Protect(secret);

    private static string SettingsJson(JsonElement settings) =>
        settings.ValueKind == JsonValueKind.Object ? settings.GetRawText() : string.Empty;
}
