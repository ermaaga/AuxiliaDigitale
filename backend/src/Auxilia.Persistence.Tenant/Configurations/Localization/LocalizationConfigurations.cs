using Auxilia.Domain.Localization;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Localization;

internal sealed class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable("languages", TenantSchemas.Localization);
        builder.HasKey(language => language.Id);
        builder.Property(language => language.Id).ValueGeneratedNever();
        builder.Property(language => language.Code).HasMaxLength(Language.CodeMaxLength);
        builder.Property(language => language.Name).HasMaxLength(Language.NameMaxLength);

        // Translations reference the code (natural key, as in the user's language_code).
        builder.HasAlternateKey(language => language.Code).HasName("ak_languages_code");
    }
}

internal sealed class ResourceKeyConfiguration : IEntityTypeConfiguration<ResourceKey>
{
    public void Configure(EntityTypeBuilder<ResourceKey> builder)
    {
        builder.ToTable("resource_keys", TenantSchemas.Localization);
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.Key).HasMaxLength(ResourceKey.KeyMaxLength);
        builder.Property(key => key.Category).HasMaxLength(ResourceKey.CategoryMaxLength);
        builder.Property(key => key.Description).HasMaxLength(ResourceKey.DescriptionMaxLength);
        builder.HasIndex(key => key.Key).IsUnique();
        builder.HasIndex(key => key.Category);

        builder.HasMany(key => key.Translations).WithOne().HasForeignKey(translation => translation.ResourceKeyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(key => key.Translations).HasField("translations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ResourceTranslationConfiguration : IEntityTypeConfiguration<ResourceTranslation>
{
    public void Configure(EntityTypeBuilder<ResourceTranslation> builder)
    {
        builder.ToTable("resource_translations", TenantSchemas.Localization);
        builder.HasKey(translation => translation.Id);
        builder.Property(translation => translation.Id).ValueGeneratedNever();
        builder.Property(translation => translation.LanguageCode).HasMaxLength(Language.CodeMaxLength);
        builder.Property(translation => translation.Value).HasMaxLength(ResourceTranslation.ValueMaxLength);
        builder.HasIndex(translation => new { translation.ResourceKeyId, translation.LanguageCode }).IsUnique();
        builder.HasOne<Language>().WithMany().HasForeignKey(translation => translation.LanguageCode)
            .HasPrincipalKey(language => language.Code).OnDelete(DeleteBehavior.Restrict);
    }
}
