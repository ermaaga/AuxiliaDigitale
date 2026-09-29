using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class ClientApplicationConfiguration : IEntityTypeConfiguration<ClientApplication>
{
    public void Configure(EntityTypeBuilder<ClientApplication> builder)
    {
        builder.ToTable("client_applications");
        builder.HasKey(client => client.Id);
        builder.Property(client => client.Id).ValueGeneratedNever();
        builder.Property(client => client.ClientId).HasMaxLength(ClientApplication.ClientIdMaxLength);
        builder.HasIndex(client => client.ClientId).IsUnique();
        builder.Property(client => client.Name).HasMaxLength(ClientApplication.NameMaxLength);
        builder.Property(client => client.Type).HasConversion<string>().HasMaxLength(CatalogConventions.EnumMaxLength);
        builder.Property(client => client.SecretHash).HasMaxLength(ClientApplication.SecretHashMaxLength);
        builder.Property(client => client.AllowedOrigins);
        builder.Property(client => client.MinAppVersion).HasMaxLength(ClientApplication.VersionMaxLength);
        builder.Property(client => client.CaptchaProvider).HasMaxLength(ClientApplication.CaptchaProviderMaxLength);
        builder.Ignore(client => client.IsConfidential);
        builder.Ignore(client => client.DomainEvents);
        builder.HasAuditColumns();
        builder.HasXminVersion();
    }
}
