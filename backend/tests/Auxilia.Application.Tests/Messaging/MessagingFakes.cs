using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Messaging;
using Auxilia.Application.Tests.Configuration;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Messaging;

/// <summary>In-memory messaging data shared by every unit of work of a test.</summary>
internal sealed class InMemoryMessagingData : IMessagingDataFactory, IMessagingData
{
    public List<MessagingAccount> Accounts { get; } = [];

    public List<SenderRule> Rules { get; } = [];

    public List<MessageTemplate> Templates { get; } = [];

    public List<OutboundMessage> Outbound { get; } = [];

    public int Saves { get; private set; }

    public Task<IMessagingData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IMessagingData>(this);

    public Task<IReadOnlyList<MessagingAccount>> AccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MessagingAccount>>(Accounts.ToList());

    public Task<MessagingAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Accounts.SingleOrDefault(account => account.Id == id));

    public void Add(MessagingAccount account) => Accounts.Add(account);

    public Task<IReadOnlyList<SenderRule>> RulesAsync(MessageChannel? channel, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SenderRule>>(Rules.Where(rule => channel is null || rule.Channel == channel).ToList());

    public void Add(SenderRule rule) => Rules.Add(rule);

    public void Remove(SenderRule rule) => Rules.Remove(rule);

    public Task<MessageTemplate?> FindTemplateAsync(MessageChannel channel, string code, string language, CancellationToken cancellationToken) =>
        Task.FromResult(Templates.SingleOrDefault(template => template.Channel == channel && template.Code == code && template.Language == language));

    public void Add(OutboundMessage message) => Outbound.Add(message);

    public Task<OutboundMessage?> FindOutboundAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Outbound.SingleOrDefault(message => message.Id == id));

    public Task<(IReadOnlyList<OutboundMessage> Items, int Total)> OutboundPageAsync(OutboundMessageFilter filter, CancellationToken cancellationToken)
    {
        var matching = Outbound
            .Where(message => filter.Channel is null || message.Channel == filter.Channel)
            .Where(message => filter.Status is null || message.Status == filter.Status)
            .Where(message => filter.AccountId is null || message.AccountId == filter.AccountId)
            .Where(message => filter.Search is null || message.Recipient.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.QueuedAt)
            .ToList();
        return Task.FromResult<(IReadOnlyList<OutboundMessage>, int)>((matching.Skip(filter.Skip).Take(filter.Take).ToList(), matching.Count));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>An e-mail adapter that records sends and can fail on demand.</summary>
internal sealed class FakeEmailChannel : IMessageChannel
{
    public string Provider => "smtp";

    public MessageChannel Channel => MessageChannel.Email;

    public List<(ChannelMessage Message, ChannelAccount Account)> Sent { get; } = [];

    public Exception? Failure { get; set; }

    public bool AreSettingsValid(string settingsJson) => settingsJson.Contains("\"host\"", StringComparison.Ordinal);

    public bool IsValidRecipient(string recipient) => recipient.Contains('@', StringComparison.Ordinal);

    public Task SendAsync(ChannelMessage message, ChannelAccount account, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        Sent.Add((message, account));
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccountSecrets : IAccountSecretProtector
{
    public string Protect(string secret) => "enc:" + secret;

    public string Unprotect(string protectedSecret) => protectedSecret[4..];
}

/// <summary>Replaces <c>{{ key }}</c> with the model value; <c>{{ broken</c> fails.</summary>
internal sealed class SimpleRenderer : ITemplateRenderer
{
    public bool IsValid(string template) => !template.Contains("{{ broken", StringComparison.Ordinal);

    public string? Render(string template, IReadOnlyDictionary<string, object?> model, bool html)
    {
        if (!IsValid(template))
        {
            return null;
        }

        foreach (var (key, value) in model)
        {
            template = template.Replace("{{ " + key + " }}", html ? System.Net.WebUtility.HtmlEncode(value?.ToString()) : value?.ToString(), StringComparison.Ordinal);
        }

        return template;
    }
}

/// <summary>The messaging services over in-memory data for tenant <c>acme</c> (default language it).</summary>
internal sealed class MessagingHarness
{
    public static readonly TenantInfo Acme = new(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");

    public MessagingHarness(params TenantRole[] roles)
    {
        TenantContext.Current.Returns(Acme);
        TenantContext.Tenant.Returns(Acme);
        User.ActorType.Returns(roles.Length > 0 ? ActorType.User : ActorType.System);
        User.Roles.Returns(roles);

        var runner = Platform.ManagerHarness.Runner();
        Snapshots = new MessagingSnapshotCache(Cache, TenantContext, Data);
        Accounts = new MessagingAccountManager(runner, Data, [Channel], Secrets, Renderer, TenantContext, Cache, TimeProvider.System, Logger);
        Query = new MessagingQueryService(Data, [Channel]);
        Dispatcher = new MessageDispatcher(runner, Data, Snapshots, [Channel], Renderer, Outbox, TenantContext, User, TimeProvider.System);
        Delivery = new OutboundMessageManager(runner, Data, [Channel], Secrets, TimeProvider.System, NullLogger<OutboundMessageManager>.Instance);
    }

    public InMemoryMessagingData Data { get; } = new();

    public FakeEmailChannel Channel { get; } = new();

    public FakeAccountSecrets Secrets { get; } = new();

    public SimpleRenderer Renderer { get; } = new();

    public FakeReferenceDataCache Cache { get; } = new();

    public ITenantContext TenantContext { get; } = Substitute.For<ITenantContext>();

    public ICurrentUser User { get; } = Substitute.For<ICurrentUser>();

    public IMessageOutbox Outbox { get; } = Substitute.For<IMessageOutbox>();

    public MessagingSnapshotCache Snapshots { get; }

    public MessagingAccountManager Accounts { get; }

    public MessagingQueryService Query { get; }

    public RecordingLogger<MessagingAccountManager> Logger { get; } = new();

    public MessageDispatcher Dispatcher { get; }

    public OutboundMessageManager Delivery { get; }

    public MessagingAccount AddAccount(string name, bool isDefault = false, bool isActive = true, MessageChannel channel = MessageChannel.Email, string provider = "smtp")
    {
        var account = MessagingAccount.Create(Guid.CreateVersion7(), channel, provider, name, """{"host":"smtp.test"}""", "enc:pw-" + name).Value;
        account.SetDefault(isDefault);
        if (!isActive)
        {
            account.SetActive(false);
        }

        Data.Accounts.Add(account);
        return account;
    }

    public void AddTemplate(string code, string language, string subject = "Hi {{ name }}", string body = "<p>{{ name }}</p>") =>
        Data.Templates.Add(new MessageTemplate(Guid.CreateVersion7(), MessageChannel.Email, code, language, subject, body, isSystem: true));
}
