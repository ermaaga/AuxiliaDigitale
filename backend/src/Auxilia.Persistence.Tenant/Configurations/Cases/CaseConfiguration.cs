using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Cases;

internal sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("cases", TenantSchemas.Cases, table =>
        {
            table.HasCheckConstraint("ck_cases_status", "status IN ('Inserted', 'InProgress', 'Sent', 'Completed')");
            table.HasCheckConstraint("ck_cases_price", "price >= 0");
        });
        builder.HasKey(@case => @case.Id);
        builder.Property(@case => @case.Id).ValueGeneratedNever();
        builder.Property(@case => @case.Number).HasMaxLength(Case.NumberMaxLength);
        builder.HasIndex(@case => @case.Number).IsUnique();
        builder.Property(@case => @case.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(@case => @case.Price).HasPrecision(12, 2);
        builder.Property(@case => @case.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(@case => @case.CustomFields).HasColumnType("jsonb");
        builder.HasOne<ClientProfile>().WithMany().HasForeignKey(@case => @case.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(@case => @case.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Specialization>().WithMany().HasForeignKey(@case => @case.SpecializationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(@case => @case.ClientId);
        builder.HasIndex(@case => @case.ServiceId);
        builder.HasIndex(@case => @case.SpecializationId);
        builder.HasIndex(@case => new { @case.Status, @case.StartedOn });

        // Default order of the lists (newest first) for every scope and status filter (H-02).
        builder.HasIndex(@case => new { @case.StartedOn, @case.Number, @case.Id });

        builder.OwnsMany(@case => @case.History, history =>
        {
            history.ToTable("case_status_history", TenantSchemas.Cases);
            history.WithOwner().HasForeignKey(change => change.CaseId);
            history.HasKey(change => change.Id);
            history.Property(change => change.Id).ValueGeneratedNever();
            history.Property(change => change.FromStatus).HasConversion<string>().HasMaxLength(20);
            history.Property(change => change.ToStatus).HasConversion<string>().HasMaxLength(20);
            history.Property(change => change.Note).HasMaxLength(Case.NoteMaxLength);
            history.HasOne<User>().WithMany().HasForeignKey(change => change.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            history.HasIndex(change => new { change.CaseId, change.Sequence }).IsUnique();
            history.HasIndex(change => change.ChangedByUserId);
        });
        builder.Navigation(@case => @case.History).HasField("history").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(@case => @case.Payments, payments =>
        {
            payments.ToTable("case_payments", TenantSchemas.Cases, table => table.HasCheckConstraint("ck_case_payments_amount", "amount > 0"));
            payments.WithOwner().HasForeignKey(payment => payment.CaseId);
            payments.HasKey(payment => payment.Id);
            payments.Property(payment => payment.Id).ValueGeneratedNever();
            payments.Property(payment => payment.Amount).HasPrecision(12, 2);
            payments.Property(payment => payment.Note).HasMaxLength(Case.NoteMaxLength);
            payments.HasOne<User>().WithMany().HasForeignKey(payment => payment.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
            payments.HasIndex(payment => payment.CaseId);
            payments.HasIndex(payment => payment.RecordedByUserId);
        });
        builder.Navigation(@case => @case.Payments).HasField("payments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>The last case number of each year (<c>cases.case_numbers</c>): one row per year, updated in the transaction.</summary>
internal sealed class CaseNumberCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}

internal sealed class CaseNumberCounterConfiguration : IEntityTypeConfiguration<CaseNumberCounter>
{
    public void Configure(EntityTypeBuilder<CaseNumberCounter> builder)
    {
        builder.ToTable("case_numbers", TenantSchemas.Cases);
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}
