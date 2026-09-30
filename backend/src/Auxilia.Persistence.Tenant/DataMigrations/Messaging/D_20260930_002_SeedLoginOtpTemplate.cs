using Auxilia.Domain.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.DataMigrations.Messaging;

/// <summary>
/// The <c>login-otp</c> e-mail template (F35, EN + IT) for tenants that already ran D_20260930_001: the same idempotent
/// upsert of every system template (customised templates are left alone). Also runs on new tenants.
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20260930_002_SeedLoginOtpTemplate : IDataMigration
{
    public string Key => "D_20260930_002";

    public string Description => "E-mail template for sign-in codes (EN, IT)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Set<MessageTemplate>().Where(template => template.Channel == MessageChannel.Email).ToListAsync(cancellationToken);
        foreach (var (code, language, subject, body) in SystemEmailTemplates.All.Where(item => item.Code == "login-otp"))
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
