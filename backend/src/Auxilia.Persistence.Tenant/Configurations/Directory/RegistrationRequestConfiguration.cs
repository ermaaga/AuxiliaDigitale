using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class RegistrationRequestConfiguration : IEntityTypeConfiguration<RegistrationRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationRequest> builder)
    {
        builder.ToTable("registration_requests", TenantSchemas.Directory, table =>
            table.HasCheckConstraint("ck_registration_requests_status", "status IN ('Pending', 'Approved', 'Rejected')"));
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.FirstName).HasMaxLength(Person.NameMaxLength);
        builder.Property(request => request.LastName).HasMaxLength(Person.NameMaxLength);
        builder.Property(request => request.Email).HasMaxLength(Person.EmailMaxLength);
        builder.Property(request => request.Phone).HasMaxLength(Person.PhoneMaxLength);
        builder.Property(request => request.FiscalCode).HasMaxLength(Person.FiscalCodeLength);
        builder.Property(request => request.Language).HasMaxLength(RegistrationRequest.LanguageMaxLength);
        builder.Property(request => request.PrivacyVersion).HasMaxLength(RegistrationRequest.PrivacyVersionMaxLength);
        builder.Property(request => request.ClientApplication).HasMaxLength(ClientApplication.ClientIdMaxLength);
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.Notes).HasMaxLength(RegistrationRequest.NotesMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(request => request.ProcessedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(request => request.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => request.ProcessedByUserId);
        builder.HasIndex(request => request.ClientId);
        builder.HasIndex(request => new { request.Status, request.RequestedAt });

        // Q05: one pending request per e-mail (stored lower case); processed ones do not count.
        builder.HasIndex(request => request.Email).IsUnique().HasFilter("status = 'Pending'");
    }
}
