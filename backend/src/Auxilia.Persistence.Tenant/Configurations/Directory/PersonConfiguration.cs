using Auxilia.Domain.Directory;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people", TenantSchemas.Directory);
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.Property(person => person.FirstName).HasMaxLength(Person.NameMaxLength);
        builder.Property(person => person.LastName).HasMaxLength(Person.NameMaxLength);
        builder.Property(person => person.Email).HasMaxLength(Person.EmailMaxLength).HasColumnType("citext");
        builder.Property(person => person.Phone).HasMaxLength(Person.PhoneMaxLength);
        builder.Property(person => person.FiscalCode).HasMaxLength(Person.FiscalCodeLength);
        builder.Property(person => person.CustomFields).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Ignore(person => person.FullName);
        builder.HasIndex(person => new { person.LastName, person.FirstName });
        builder.HasIndex(person => person.Email);

        // Q54: one fiscal code per person among the people not deleted (a deleted client frees it).
        builder.HasIndex(person => person.FiscalCode).IsUnique().HasFilter("fiscal_code IS NOT NULL AND NOT is_deleted");
        builder.HasIndex(person => person.CustomFields).HasMethod("gin");
    }
}
