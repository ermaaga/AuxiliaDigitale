using System.Text.Json;

using Auxilia.Application.Messaging;
using Auxilia.Application.Messaging.Public;
using Auxilia.Domain.Messaging;
using Auxilia.Infrastructure.Adapters.Channels.Smtp;
using Auxilia.Persistence.Tenant.DataMigrations;
using Auxilia.Tests.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Worker.IntegrationTests;

/// <summary>
/// N03 end to end: an account created through the manager (secret protected), a message queued by another module
/// through <see cref="IMessageDispatcher"/>, delivered by the Worker through SMTP and recorded as Sent.
/// </summary>
[Collection(BusGroup.Name)]
public sealed class OutboundMessagingTests(BusFixture bus) : IAsyncDisposable
{
    private readonly FakeSmtpServer smtp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task QueuedMessage_IsDeliveredBySmtpAndRecordedAsSent()
    {
        await SeedTemplatesAsync();
        var settings = JsonSerializer.SerializeToElement(new SmtpSettings("127.0.0.1", smtp.Port, SmtpSecurity.None, "office", "noreply@studio.test", "Studio"), SmtpSettings.Json);

        Guid accountId;
        Guid messageId;
        await using (var scope = await bus.TenantScopeAsync())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IMessagingAccountManager>();
            accountId = (await accounts.CreateAccountAsync(new CreateMessagingAccount(MessageChannel.Email, "smtp", "Office " + Guid.NewGuid().ToString("N")[..6], settings, FakeSmtpServer.Password), Ct)).Value;
            (await accounts.SetDefaultAccountAsync(accountId, Ct)).IsSuccess.ShouldBeTrue();

            var queued = await scope.ServiceProvider.GetRequiredService<IMessageDispatcher>().QueueAsync(new OutboundMessageRequest(
                MessageChannel.Email, MessagePurpose.Transactional, "anna@example.test", MessageTemplates.PasswordReset, "it",
                new Dictionary<string, object?> { ["name"] = "Anna", ["appName"] = "Studio", ["link"] = "https://studio.test/reset", ["expiresAt"] = "domani" }), Ct);
            queued.IsSuccess.ShouldBeTrue();
            messageId = queued.Value;
        }

        await using var db = bus.TenantDb();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        OutboundMessage? message;
        do
        {
            await Task.Delay(200, Ct);
            message = await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(item => item.Id == messageId, Ct);
        }
        while (message.Status == OutboundMessageStatus.Queued && DateTime.UtcNow < deadline);

        (message.Status, message.AccountId, message.Language).ShouldBe((OutboundMessageStatus.Sent, accountId, "it"));
        var mail = smtp.Messages.ShouldHaveSingleItem();
        mail.Recipients.ShouldBe(["anna@example.test"]);
        mail.Data.ShouldContain("Reimposta la password di Studio");
        (await db.Set<MessagingAccount>().AsNoTracking().SingleAsync(item => item.Id == accountId, Ct)).SecretProtected!.ShouldNotContain(FakeSmtpServer.Password);
    }

    public ValueTask DisposeAsync() => smtp.DisposeAsync();

    private async Task SeedTemplatesAsync()
    {
        await using var db = bus.TenantDb();
        await bus.Api.GetRequiredService<DataMigrationRunner>().ApplyPendingAsync(db, Ct);
    }
}
