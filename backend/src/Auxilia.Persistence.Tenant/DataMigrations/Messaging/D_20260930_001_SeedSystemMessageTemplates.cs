using Auxilia.Domain.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.DataMigrations.Messaging;

/// <summary>
/// System e-mail templates in EN and IT (N03, skill auxilia-localization). Idempotent: inserts missing templates by
/// (channel, code, language); existing ones are upgraded only if the tenant has not customised them. Also runs on new
/// tenants (not covered by the initial seed).
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_001_SeedSystemMessageTemplates : IDataMigration
{
    public string Key => "D_20260930_001";

    public string Description => "System e-mail templates (EN, IT)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Set<MessageTemplate>().Where(template => template.Channel == MessageChannel.Email).ToListAsync(cancellationToken);
        foreach (var (code, language, subject, body) in SystemEmailTemplates.All)
        {
            var template = existing.SingleOrDefault(item => item.Code == code && item.Language == language);
            if (template is null)
            {
                db.Set<MessageTemplate>().Add(new MessageTemplate(Guid.CreateVersion7(), MessageChannel.Email, code, language, subject, body, isSystem: true));
            }
            else if (template.IsSystem)
            {
                template.UpgradeSystemContent(subject, body);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Shipped content of the system e-mail templates (Liquid).</summary>
internal static class SystemEmailTemplates
{
    public static readonly (string Code, string Language, string Subject, string Body)[] All =
    [
        ("account-activation", "en", "Activate your {{ appName }} account",
            "<p>Hello {{ name }},</p><p>an account has been created for you on {{ appName }}. Activate it and choose your password:</p><p><a href=\"{{ link }}\">Activate account</a></p><p>The link expires on {{ expiresAt }}.</p>"),
        ("account-activation", "it", "Attiva il tuo account {{ appName }}",
            "<p>Ciao {{ name }},</p><p>è stato creato un account per te su {{ appName }}. Attivalo e scegli la tua password:</p><p><a href=\"{{ link }}\">Attiva account</a></p><p>Il link scade il {{ expiresAt }}.</p>"),
        ("password-reset", "en", "Reset your {{ appName }} password",
            "<p>Hello {{ name }},</p><p>we received a request to reset your password. Choose a new one here:</p><p><a href=\"{{ link }}\">Reset password</a></p><p>If you did not ask for it, ignore this e-mail. The link expires on {{ expiresAt }}.</p>"),
        ("password-reset", "it", "Reimposta la password di {{ appName }}",
            "<p>Ciao {{ name }},</p><p>abbiamo ricevuto una richiesta di reimpostazione della password. Scegline una nuova qui:</p><p><a href=\"{{ link }}\">Reimposta password</a></p><p>Se non l'hai chiesto tu, ignora questa e-mail. Il link scade il {{ expiresAt }}.</p>"),
        ("registration-received", "en", "We received your registration request",
            "<p>Hello {{ name }},</p><p>thank you for registering with {{ appName }}. We will review your request and let you know soon.</p>"),
        ("registration-received", "it", "Abbiamo ricevuto la tua richiesta di registrazione",
            "<p>Ciao {{ name }},</p><p>grazie per esserti registrato su {{ appName }}. Esamineremo la tua richiesta e ti faremo sapere al più presto.</p>"),
        ("case-expiry-reminder", "en", "Your {{ serviceName }} expires on {{ endDate }}",
            "<p>Hello {{ name }},</p><p>your {{ serviceName }} expires on {{ endDate }}. Contact us to renew it.</p>"),
        ("case-expiry-reminder", "it", "Il tuo {{ serviceName }} scade il {{ endDate }}",
            "<p>Ciao {{ name }},</p><p>il tuo {{ serviceName }} scade il {{ endDate }}. Contattaci per rinnovarlo.</p>"),
        ("request-reply", "en", "New reply to your request",
            "<p>Hello {{ name }},</p><p>{{ responderName }} replied to your request \"{{ requestTitle }}\".</p><p><a href=\"{{ link }}\">Open the request</a></p>"),
        ("request-reply", "it", "Nuova risposta alla tua richiesta",
            "<p>Ciao {{ name }},</p><p>{{ responderName }} ha risposto alla tua richiesta \"{{ requestTitle }}\".</p><p><a href=\"{{ link }}\">Apri la richiesta</a></p>"),
        ("account-test", "en", "Test message from {{ accountName }}",
            "<p>This is a test message sent through the account \"{{ accountName }}\" of {{ tenantName }}. If you received it, the account works.</p>"),
        ("account-test", "it", "Messaggio di prova da {{ accountName }}",
            "<p>Questo è un messaggio di prova inviato tramite l'account \"{{ accountName }}\" di {{ tenantName }}. Se lo hai ricevuto, l'account funziona.</p>"),
    ];
}
