using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", TenantSchemas.Directory);
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.Name).HasMaxLength(Tag.NameMaxLength).HasColumnType("citext");
        builder.Property(tag => tag.Color).HasMaxLength(7);
        builder.HasIndex(tag => tag.Name).IsUnique();
    }
}

internal sealed class PersonTagConfiguration : IEntityTypeConfiguration<PersonTag>
{
    public void Configure(EntityTypeBuilder<PersonTag> builder)
    {
        builder.ToTable("person_tags", TenantSchemas.Directory);
        builder.HasKey(assignment => new { assignment.PersonId, assignment.TagId });
        builder.HasOne<Person>().WithMany().HasForeignKey(assignment => assignment.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tag>().WithMany().HasForeignKey(assignment => assignment.TagId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(assignment => assignment.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(assignment => assignment.TagId);
        builder.HasIndex(assignment => assignment.AssignedByUserId);
    }
}

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("consents", TenantSchemas.Directory, table =>
        {
            table.HasCheckConstraint("ck_consents_purpose", "purpose IN ('Marketing', 'Privacy')");
            table.HasCheckConstraint("ck_consents_channel", "channel IN ('Email', 'WhatsApp')");
            table.HasCheckConstraint("ck_consents_source", "source IN ('Staff', 'Import', 'Api', 'LegacyMigration')");
        });
        builder.HasKey(consent => consent.Id);
        builder.Property(consent => consent.Id).ValueGeneratedNever();
        builder.Property(consent => consent.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(consent => consent.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(consent => consent.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(consent => consent.Version).HasMaxLength(Consent.VersionMaxLength);
        builder.Property(consent => consent.Note).HasMaxLength(Consent.NoteMaxLength);
        builder.HasOne<Person>().WithMany().HasForeignKey(consent => consent.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(consent => consent.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(consent => new { consent.PersonId, consent.Purpose, consent.Channel, consent.RecordedAt });
        builder.HasIndex(consent => consent.RecordedByUserId);
    }
}
