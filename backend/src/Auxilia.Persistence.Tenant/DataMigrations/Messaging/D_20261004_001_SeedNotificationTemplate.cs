using Auxilia.Domain.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.DataMigrations.Messaging;

/// <summary>
/// The <c>notification</c> e-mail template (F16, EN + IT: notifications by e-mail when the preference asks for it) for
/// tenants that already ran D_20260930_001: the same idempotent upsert (customised templates are left alone).
/// </summary>
[IncludedInInitialSeed(false)]
internal sealed class D_20261004_001_SeedNotificationTemplate : IDataMigration
{
    public string Key => "D_20261004_001";

    public string Description => "E-mail template for notifications (EN, IT)";

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var existing = await db.Set<MessageTemplate>().Where(template => template.Channel == MessageChannel.Email).ToListAsync(cancellationToken);
        foreach (var (code, language, subject, body) in SystemEmailTemplates.All.Where(item => item.Code == "notification"))
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
