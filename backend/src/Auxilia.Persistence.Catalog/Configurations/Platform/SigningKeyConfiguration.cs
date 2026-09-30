using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.ToTable("signing_keys");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).HasMaxLength(SigningKey.IdLength);
        builder.Property(key => key.Algorithm).HasMaxLength(10);
        builder.Property(key => key.PublicJwk).HasColumnType("jsonb");
        builder.Property(key => key.PrivateKeyProtected).HasMaxLength(4000);
        builder.Ignore(key => key.IsActive);

        // At most one active (signing) key.
        // NULLS NOT DISTINCT: otherwise every NULL retired_at is distinct and the index would allow several active keys.
        builder.HasIndex(key => key.RetiredAt).IsUnique().AreNullsDistinct(false).HasFilter("retired_at IS NULL").HasDatabaseName("ux_signing_keys_active");
        builder.HasXminVersion();
    }
}
