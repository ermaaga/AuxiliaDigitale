using Auxilia.Application.Abstractions.Channels;
using Auxilia.Domain.Messaging;
using Auxilia.Persistence.Tenant.Messaging;
using Auxilia.Persistence.Tenant.DataMigrations.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Messaging tables: system templates seeded idempotently, one default account per channel, the outbound log query.</summary>
[Collection(TenantDatabaseGroup.Name)]
public sealed class MessagingPersistenceTests(TenantDatabaseFixture database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TemplateSeed_IsIdempotentCoversEnAndItAndKeepsCustomisedTemplates()
    {
        var seed = new D_20260930_001_SeedSystemMessageTemplates();
        await using (var db = database.CreateContext())
        {
            await seed.ApplyAsync(db, Ct);
        }

        await using (var db = database.CreateContext())
        {
            var reset = await db.Set<MessageTemplate>().SingleAsync(item => item.Code == "password-reset" && item.Language == "it", Ct);
            reset.Customize("Il nostro oggetto", "<p>Il nostro testo</p>");
            await db.SaveChangesAsync(Ct);
            await seed.ApplyAsync(db, Ct);
        }

        await using var read = database.CreateContext();
        var templates = await read.Set<MessageTemplate>().AsNoTracking().ToListAsync(Ct);
        templates.Count.ShouldBe(16);
        templates.GroupBy(item => item.Code).Select(group => string.Join(',', group.Select(item => item.Language).Order())).ShouldAllBe(languages => languages == "en,it");
        templates.Single(item => item.Code == "password-reset" && item.Language == "it").Subject.ShouldBe("Il nostro oggetto");
        templates.ShouldAllBe(item => item.IsSystem && item.Channel == MessageChannel.Email);
    }

    [Fact]
    public async Task OnlyOneDefaultAccountPerChannel()
    {
        await using var db = database.CreateContext();
        var channel = MessageChannel.Sms;
        foreach (var name in new[] { "first", "second" })
        {
            var account = MessagingAccount.Create(Guid.CreateVersion7(), channel, "sms-gateway", name, """{"url":"https://sms.test"}""", null).Value;
            account.SetDefault(true);
            db.Set<MessagingAccount>().Add(account);
        }

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task OutboundPage_FiltersInSqlNewestFirstAndEscapesTheSearch()
    {
        var account = MessagingAccount.Create(Guid.CreateVersion7(), MessageChannel.Email, "smtp", "Log test", "{}", null).Value;
        var accountId = account.Id;
        var marker = Guid.NewGuid().ToString("N")[..8];
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        await using (var db = database.CreateContext())
        {
            db.Set<MessagingAccount>().Add(account);
            foreach (var (recipient, minutes, sent) in new[] { ($"a_{marker}@x.test", 1, true), ($"ab{marker}@x.test", 2, false), ($"50%{marker}@x.test", 3, false) })
            {
                var message = new OutboundMessage(Guid.CreateVersion7(), MessageChannel.Email, MessagePurpose.Notification, accountId, recipient, "code", "it", "s", "b", start.AddMinutes(minutes));
                if (sent)
                {
                    message.MarkSent(start.AddMinutes(minutes));
                }

                db.Set<OutboundMessage>().Add(message);
            }

            await db.SaveChangesAsync(Ct);
        }

        await using var data = new MessagingData(database.CreateContext());
        var all = await data.OutboundPageAsync(new OutboundMessageFilter(null, null, marker, accountId, 0, 2), Ct);
        all.Total.ShouldBe(3);
        all.Items.Select(item => item.Recipient).ShouldBe([$"50%{marker}@x.test", $"ab{marker}@x.test"]);

        // `_` and `%` are literal characters of the search, not wildcards.
        (await data.OutboundPageAsync(new OutboundMessageFilter(null, null, "A_" + marker, null, 0, 10), Ct)).Items.ShouldHaveSingleItem().Recipient.ShouldBe($"a_{marker}@x.test");
        (await data.OutboundPageAsync(new OutboundMessageFilter(null, null, "%" + marker, null, 0, 10), Ct)).Items.ShouldHaveSingleItem();
        (await data.OutboundPageAsync(new OutboundMessageFilter(MessageChannel.Email, OutboundMessageStatus.Sent, marker, accountId, 0, 10), Ct)).Total.ShouldBe(1);
        (await data.OutboundPageAsync(new OutboundMessageFilter(MessageChannel.WhatsApp, null, marker, null, 0, 10), Ct)).Total.ShouldBe(0);
    }
}
