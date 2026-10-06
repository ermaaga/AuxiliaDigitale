using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>
/// Read-only EF Core view of the legacy database (schema <c>public</c>, PascalCase names as the legacy EF model created
/// them). No tracking, never saved: the import only reads it. Tables the database does not have (Security_Update,
/// ImportJobs — <see cref="LegacyVariant"/>) are left out of the model.
/// </summary>
internal sealed class LegacyDbContext : DbContext
{
    public LegacyDbContext(DbContextOptions<LegacyDbContext> options, LegacyVariant variant)
        : base(options)
    {
        Variant = variant;
    }

    public LegacyVariant Variant { get; }

    public IQueryable<LegacyUser> Users => Set<LegacyUser>();

    public IQueryable<LegacyRole> Roles => Set<LegacyRole>();

    public IQueryable<LegacyUserRole> UserRoles => Set<LegacyUserRole>();

    public IQueryable<LegacyRoleSpecialization> RoleSpecializations => Set<LegacyRoleSpecialization>();

    public IQueryable<LegacyUserRoleSpecialization> UserRoleSpecializations => Set<LegacyUserRoleSpecialization>();

    public IQueryable<LegacyMembershipType> MembershipTypes => Set<LegacyMembershipType>();

    public IQueryable<LegacyMembership> Memberships => Set<LegacyMembership>();

    public IQueryable<LegacyMembershipFolderTemplate> MembershipFolderTemplates => Set<LegacyMembershipFolderTemplate>();

    public IQueryable<LegacySubscription> Subscriptions => Set<LegacySubscription>();

    public IQueryable<LegacyUserDocument> UserDocuments => Set<LegacyUserDocument>();

    public IQueryable<LegacyAppointment> Appointments => Set<LegacyAppointment>();

    public IQueryable<LegacyRequest> Requests => Set<LegacyRequest>();

    public IQueryable<LegacyNotification> Notifications => Set<LegacyNotification>();

    public IQueryable<LegacyRegistrationRequest> RegistrationRequests => Set<LegacyRegistrationRequest>();

    public IQueryable<LegacyImportType> ImportTypes => Set<LegacyImportType>();

    public IQueryable<LegacyImport> Imports => Set<LegacyImport>();

    /// <summary>Only when the database has the table (<see cref="LegacyVariant.ImportJobs"/>, Q61).</summary>
    public IQueryable<LegacyImportJob> ImportJobs => Set<LegacyImportJob>();

    public IQueryable<LegacyLanguage> Languages => Set<LegacyLanguage>();

    public IQueryable<LegacyResourceKey> ResourceKeys => Set<LegacyResourceKey>();

    public IQueryable<LegacyResourceTranslation> ResourceTranslations => Set<LegacyResourceTranslation>();

    public IQueryable<LegacySystemConfiguration> SystemConfigurations => Set<LegacySystemConfiguration>();

    public IQueryable<LegacyEmailConfiguration> EmailConfigurations => Set<LegacyEmailConfiguration>();

    public IQueryable<LegacyModuleConfiguration> ModuleConfigurations => Set<LegacyModuleConfiguration>();

    public IQueryable<LegacyPageConfiguration> PageConfigurations => Set<LegacyPageConfiguration>();

    public IQueryable<LegacyEntityConfiguration> EntityConfigurations => Set<LegacyEntityConfiguration>();

    /// <summary>Security_Update only (<see cref="LegacyVariant.SecurityUpdate"/>).</summary>
    public IQueryable<LegacyLoginAuditLog> LoginAuditLogs => Set<LegacyLoginAuditLog>();

    /// <summary>Security_Update only (<see cref="LegacyVariant.SecurityUpdate"/>).</summary>
    public IQueryable<LegacyPasswordHistory> PasswordHistories => Set<LegacyPasswordHistory>();

    public static DbContextOptions<LegacyDbContext> Options(Npgsql.NpgsqlDataSource source) =>
        new DbContextOptionsBuilder<LegacyDbContext>()
            .UseNpgsql(source)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .ReplaceService<IModelCacheKeyFactory, SchemaModelCacheKeyFactory>()
            .Options;

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("The legacy database is read-only.");

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The legacy database is read-only.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("public");
        modelBuilder.Entity<LegacyUser>(user =>
        {
            user.ToTable("Users");
            user.Property(row => row.CustomFields).HasColumnType("jsonb");
            if (!Variant.SecurityUpdate)
            {
                user.Ignore(row => row.PasswordChangedAt);
            }
        });
        modelBuilder.Entity<LegacyRole>().ToTable("Roles");
        modelBuilder.Entity<LegacyUserRole>().ToTable("UserRoles").HasKey(row => new { row.UserId, row.RoleId });
        modelBuilder.Entity<LegacyRoleSpecialization>().ToTable("RoleSpecializations");
        modelBuilder.Entity<LegacyUserRoleSpecialization>().ToTable("UserRoleSpecializations")
            .HasKey(row => new { row.UserId, row.RoleSpecializationId });
        modelBuilder.Entity<LegacyMembershipType>().ToTable("MembershipTypes");
        modelBuilder.Entity<LegacyMembership>().ToTable("Memberships");
        modelBuilder.Entity<LegacyMembershipFolderTemplate>().ToTable("MembershipFolderTemplates");
        modelBuilder.Entity<LegacySubscription>(subscription =>
        {
            subscription.ToTable("Subscriptions");
            subscription.Property(row => row.CustomFields).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LegacyUserDocument>(document =>
        {
            document.ToTable("UserDocuments");
            document.Property(row => row.CustomFields).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LegacyAppointment>(appointment =>
        {
            appointment.ToTable("Appointments");
            appointment.Property(row => row.CustomFields).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LegacyRequest>().ToTable("Requests");
        modelBuilder.Entity<LegacyNotification>().ToTable("Notifications");
        modelBuilder.Entity<LegacyRegistrationRequest>().ToTable("RegistrationRequests");
        modelBuilder.Entity<LegacyImportType>().ToTable("ImportTypes");
        modelBuilder.Entity<LegacyImport>().ToTable("Imports");
        if (Variant.ImportJobs)
        {
            modelBuilder.Entity<LegacyImportJob>().ToTable("ImportJobs");
        }

        modelBuilder.Entity<LegacyLanguage>().ToTable("Languages");
        modelBuilder.Entity<LegacyResourceKey>().ToTable("ResourceKeys");
        modelBuilder.Entity<LegacyResourceTranslation>().ToTable("ResourceTranslations");
        modelBuilder.Entity<LegacySystemConfiguration>().ToTable("SystemConfigurations");
        modelBuilder.Entity<LegacyEmailConfiguration>().ToTable("EmailConfigurations");
        modelBuilder.Entity<LegacyModuleConfiguration>().ToTable("ModuleConfigurations");
        modelBuilder.Entity<LegacyPageConfiguration>(page =>
        {
            page.ToTable("PageConfigurations");
            page.Property(row => row.ConfigurationGrid).HasColumnType("jsonb");
        });
        modelBuilder.Entity<LegacyEntityConfiguration>(entity =>
        {
            entity.ToTable("EntityConfigurations");
            entity.Property(row => row.Configuration).HasColumnType("jsonb");
        });
        if (Variant.SecurityUpdate)
        {
            modelBuilder.Entity<LegacyLoginAuditLog>().ToTable("LoginAuditLogs");
            modelBuilder.Entity<LegacyPasswordHistory>().ToTable("PasswordHistories");
        }
    }

    /// <summary>One model per <see cref="LegacyVariant"/>.</summary>
    private sealed class SchemaModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((LegacyDbContext)context).Variant, designTime);
    }
}

/// <summary>The optional parts of a legacy database.</summary>
/// <param name="SecurityUpdate">Tables and column of the legacy branch Security_Update (D-30).</param>
/// <param name="ImportJobs">The <c>ImportJobs</c> table, never created by a legacy migration (Q61).</param>
internal sealed record LegacyVariant(bool SecurityUpdate, bool ImportJobs);
