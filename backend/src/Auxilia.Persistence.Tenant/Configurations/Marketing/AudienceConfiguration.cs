using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Marketing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Marketing;

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        builder.ToTable("segments", TenantSchemas.Marketing);
        builder.HasKey(segment => segment.Id);
        builder.Property(segment => segment.Id).ValueGeneratedNever();
        builder.Property(segment => segment.Name).HasMaxLength(Segment.NameMaxLength).HasColumnType("citext");
        builder.Property(segment => segment.Description).HasMaxLength(Segment.DescriptionMaxLength);
        builder.Property(segment => segment.Rule).HasColumnType("jsonb");
        builder.HasIndex(segment => segment.Name).IsUnique();
    }
}

internal sealed class StaticListConfiguration : IEntityTypeConfiguration<StaticList>
{
    public void Configure(EntityTypeBuilder<StaticList> builder)
    {
        builder.ToTable("static_lists", TenantSchemas.Marketing);
        builder.HasKey(list => list.Id);
        builder.Property(list => list.Id).ValueGeneratedNever();
        builder.Property(list => list.Name).HasMaxLength(StaticList.NameMaxLength).HasColumnType("citext");
        builder.Property(list => list.Description).HasMaxLength(StaticList.DescriptionMaxLength);
        builder.HasIndex(list => list.Name).IsUnique();
    }
}

internal sealed class StaticListMemberConfiguration : IEntityTypeConfiguration<StaticListMember>
{
    public void Configure(EntityTypeBuilder<StaticListMember> builder)
    {
        builder.ToTable("static_list_members", TenantSchemas.Marketing);
        builder.HasKey(member => new { member.ListId, member.PersonId });
        builder.HasOne<StaticList>().WithMany().HasForeignKey(member => member.ListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Person>().WithMany().HasForeignKey(member => member.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(member => member.AddedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(member => member.PersonId);
        builder.HasIndex(member => member.AddedByUserId);
    }
}
