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
        builder.HasIndex(person => new { person.LastName, person.FirstName });
        builder.HasIndex(person => person.Email);
    }
}
