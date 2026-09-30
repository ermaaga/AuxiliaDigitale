using Auxilia.Domain.Messaging;
using Auxilia.Persistence.Tenant.DataMigrations.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tests.Tenant;

/// <summary>Messaging tables: system templates seeded idempotently, one default account per channel.</summary>
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
        templates.Count.ShouldBe(12);
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
}
