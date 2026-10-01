using System.Text.Json;

using Auxilia.Application.Abstractions.Channels;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Messaging;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Messaging;

/// <summary>Sending accounts, sender rules and outbound log of the current tenant, as the System sees them (S-03, N03).</summary>
public interface IMessagingQueryService
{
    /// <summary>Accounts by channel then name; secrets never leave, only whether one is stored.</summary>
    Task<IReadOnlyList<MessagingAccountResponse>> ListAccountsAsync(CancellationToken cancellationToken);

    /// <summary>Rules of one channel, or of every channel; ordered by channel, purpose, role (any last), priority.</summary>
    Task<IReadOnlyList<SenderRuleResponse>> ListRulesAsync(MessageChannel? channel, CancellationToken cancellationToken);

    Task<Result<PagedResponse<OutboundMessageResponse>>> ListOutboundAsync(OutboundMessageQuery query, CancellationToken cancellationToken);
}

/// <param name="Search">Part of the recipient.</param>
public sealed record OutboundMessageQuery(string? Channel, string? Status, string? Search, Guid? AccountId, int Page, int PageSize);

/// <summary>Channel and purpose names of requests (case-sensitive enum names, as the API returns them).</summary>
public static class MessagingInput
{
    public static bool TryParseChannel(string? value, out MessageChannel channel) => TryParse(value, out channel);

    public static bool TryParsePurpose(string? value, out MessagePurpose purpose) => TryParse(value, out purpose);

    public static Error InvalidField(string field) =>
        Errors.Host.ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["validation.messaging." + field] });

    private static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        return value is not null
            && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, ignoreCase: false, out result);
    }
}

internal sealed class MessagingQueryService(IMessagingDataFactory data, IEnumerable<IMessageChannel> channels) : IMessagingQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    public async Task<IReadOnlyList<MessagingAccountResponse>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.AccountsAsync(cancellationToken))
            .OrderBy(account => account.Channel)
            .ThenBy(account => account.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(account => new MessagingAccountResponse(
                account.Id,
                account.Channel.ToString(),
                account.Provider,
                account.Name,
                Settings(account.SettingsJson),
                account.SecretProtected is not null,
                account.IsDefault,
                account.IsActive,
                channels.Any(channel => string.Equals(channel.Provider, account.Provider, StringComparison.Ordinal))))
            .ToArray();
    }

    public async Task<IReadOnlyList<SenderRuleResponse>> ListRulesAsync(MessageChannel? channel, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.RulesAsync(channel, cancellationToken))
            .OrderBy(rule => rule.Channel)
            .ThenBy(rule => rule.Purpose)
            .ThenBy(rule => rule.Role is null)
            .ThenBy(rule => rule.Role, StringComparer.Ordinal)
            .ThenBy(rule => rule.Priority)
            .Select(rule => new SenderRuleResponse(rule.Channel.ToString(), rule.Purpose.ToString(), rule.Role, rule.AccountId, rule.Priority))
            .ToArray();
    }

    public async Task<Result<PagedResponse<OutboundMessageResponse>>> ListOutboundAsync(OutboundMessageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        MessageChannel? channel = null;
        if (query.Channel is not null)
        {
            if (MessagingInput.TryParseChannel(query.Channel, out var parsed))
            {
                channel = parsed;
            }
            else
            {
                errors["channel"] = ["validation.messaging.channel"];
            }
        }

        OutboundMessageStatus? status = null;
        if (query.Status is not null)
        {
            if (Enum.GetNames<OutboundMessageStatus>().Contains(query.Status, StringComparer.Ordinal))
            {
                status = Enum.Parse<OutboundMessageStatus>(query.Status);
            }
            else
            {
                errors["status"] = ["validation.messaging.status"];
            }
        }

        if (query.Search is { Length: > MaxSearchLength })
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var (items, total) = await store.OutboundPageAsync(
            new OutboundMessageFilter(channel, status, search, query.AccountId, (query.Page - 1) * query.PageSize, query.PageSize), cancellationToken);
        return new PagedResponse<OutboundMessageResponse>(
            items.Select(message => new OutboundMessageResponse(
                message.Id,
                message.Channel.ToString(),
                message.Purpose.ToString(),
                message.AccountId,
                message.Recipient,
                message.TemplateCode,
                message.Language,
                message.Status.ToString(),
                message.Attempts,
                message.ErrorCode,
                message.QueuedAt,
                message.SentAt ?? message.FailedAt)).ToArray(),
            query.Page,
            query.PageSize,
            total);
    }

    private static JsonElement Settings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }
}
