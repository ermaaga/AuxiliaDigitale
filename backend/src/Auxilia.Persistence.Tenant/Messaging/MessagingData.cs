using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Messaging;

internal sealed class MessagingDataFactory(ITenantDbContextFactory databases) : IMessagingDataFactory
{
    public async Task<IMessagingData> OpenAsync(CancellationToken cancellationToken) =>
        new MessagingData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IMessagingData"/>
internal sealed class MessagingData(ITenantDbContext db) : IMessagingData
{
    public async Task<IReadOnlyList<MessagingAccount>> AccountsAsync(CancellationToken cancellationToken) =>
        await db.Set<MessagingAccount>().OrderBy(account => account.Name).ToListAsync(cancellationToken);

    public Task<MessagingAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<MessagingAccount>().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public void Add(MessagingAccount account) => db.Set<MessagingAccount>().Add(account);

    public async Task<IReadOnlyList<SenderRule>> RulesAsync(MessageChannel? channel, CancellationToken cancellationToken) =>
        await db.Set<SenderRule>().Where(rule => channel == null || rule.Channel == channel).ToListAsync(cancellationToken);

    public void Add(SenderRule rule) => db.Set<SenderRule>().Add(rule);

    public void Remove(SenderRule rule) => db.Set<SenderRule>().Remove(rule);

    public Task<MessageTemplate?> FindTemplateAsync(MessageChannel channel, string code, string language, CancellationToken cancellationToken) =>
        db.Set<MessageTemplate>().AsNoTracking()
            .SingleOrDefaultAsync(template => template.Channel == channel && template.Code == code && template.Language == language, cancellationToken);

    public void Add(OutboundMessage message) => db.Set<OutboundMessage>().Add(message);

    public Task<OutboundMessage?> FindOutboundAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<OutboundMessage>().SingleOrDefaultAsync(message => message.Id == id, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
