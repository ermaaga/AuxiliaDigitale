namespace Auxilia.MigrationRunner.LegacyImport;

/// <summary>What happens to a legacy table (docs/migration/mapping.md).</summary>
internal enum LegacyDisposition
{
    /// <summary>Copied into the tenant database by the task in <see cref="LegacyTable.Task"/>.</summary>
    Migrated,

    /// <summary>Read only to configure the tenant (settings, accounts, permissions, grids, translations).</summary>
    Configuration,

    /// <summary>Not migrated, by decision (<see cref="LegacyTable.Note"/>); its rows are counted in the report.</summary>
    Excluded,
}

/// <param name="Name">Legacy table name (PascalCase, schema <c>public</c>).</param>
/// <param name="Target">New table(s), or why it is not migrated.</param>
/// <param name="Task">Plan task that migrates it (E-02…E-05), empty when excluded.</param>
/// <param name="SecurityUpdate">Only in databases with the legacy branch <c>Security_Update</c> (D-30).</param>
/// <param name="Optional">In the legacy model but not created by any legacy migration (Q61): read when present.</param>
internal sealed record LegacyTable(
    string Name, LegacyDisposition Disposition, string Target, string Task, string Note = "", bool SecurityUpdate = false, bool Optional = false);

/// <summary>Every table of the legacy baseline (D-30) and its fate: the single list used by inspect, import and tests.</summary>
internal static class LegacyTables
{
    public static readonly IReadOnlyList<LegacyTable> All =
    [
        new("Users", LegacyDisposition.Migrated, "directory.people, identity.users, client_profiles, employee_profiles, client_assignments", "E-02",
            "the seeded `system` user (SystemConfigurator) is not migrated (D-18)"),
        new("Roles", LegacyDisposition.Migrated, "identity.roles (by name)", "E-02", "SystemConfigurator is not migrated (D-18)"),
        new("UserRoles", LegacyDisposition.Migrated, "identity.user_roles", "E-02"),
        new("RoleSpecializations", LegacyDisposition.Migrated, "directory.specializations", "E-02"),
        new("UserRoleSpecializations", LegacyDisposition.Migrated, "directory.specialization_members", "E-02"),
        new("PasswordHistories", LegacyDisposition.Migrated, "identity.password_history (LegacyBcrypt)", "E-02", SecurityUpdate: true),
        new("LoginAuditLogs", LegacyDisposition.Migrated, "identity.login_attempts", "E-02", SecurityUpdate: true),
        new("MembershipTypes", LegacyDisposition.Migrated, "cases.service_categories", "E-03"),
        new("Memberships", LegacyDisposition.Migrated, "cases.services", "E-03"),
        new("MembershipFolderTemplates", LegacyDisposition.Migrated, "cases.service_folders", "E-03"),
        new("Subscriptions", LegacyDisposition.Migrated, "cases.cases, case_payments, case_status_history, case_numbers", "E-03"),
        new("UserDocuments", LegacyDisposition.Migrated, "documents.documents, document_areas (+ files)", "E-04"),
        new("Appointments", LegacyDisposition.Migrated, "scheduling.appointments, appointment_status_history", "E-05"),
        new("Requests", LegacyDisposition.Migrated, "engagement.requests, request_messages", "E-05"),
        new("Notifications", LegacyDisposition.Migrated, "engagement.notifications", "E-05"),
        new("RegistrationRequests", LegacyDisposition.Migrated, "directory.registration_requests", "E-05"),
        new("ImportTypes", LegacyDisposition.Migrated, "imports.import_types", "E-05", "history only"),
        new("Imports", LegacyDisposition.Migrated, "imports.import_jobs", "E-05", "history only, no row data"),
        new("ImportJobs", LegacyDisposition.Migrated, "imports.import_jobs", "E-05", "history only, no row data; often absent (Q61)",
            Optional: true),
        new("Languages", LegacyDisposition.Configuration, "localization.languages", "E-05"),
        new("ResourceKeys", LegacyDisposition.Configuration, "localization.resource_keys", "E-05"),
        new("ResourceTranslations", LegacyDisposition.Configuration, "localization.resource_translations (is_customized)", "E-05"),
        new("SystemConfigurations", LegacyDisposition.Configuration, "configuration.settings, branding_assets", "E-05"),
        new("EmailConfigurations", LegacyDisposition.Configuration, "configuration.messaging_accounts, sender_rules", "E-05",
            "password re-encrypted with Data Protection"),
        new("ModuleConfigurations", LegacyDisposition.Configuration, "catalog.tenant_module_overrides, identity.role_permissions", "E-05"),
        new("PageConfigurations", LegacyDisposition.Configuration, "identity.role_permissions, configuration.grid_layouts", "E-05"),
        new("EntityConfigurations", LegacyDisposition.Configuration, "configuration.custom_field_definitions", "E-05"),
        new("WorkoutPlans", LegacyDisposition.Excluded, "not migrated", string.Empty, "workout plans removed (D-09)"),
        new("UserSessions", LegacyDisposition.Excluded, "not migrated", string.Empty, "sessions end at the cutover"),
        new("AppLogs", LegacyDisposition.Excluded, "not migrated", string.Empty, "archived with the legacy backup (R-03)"),
        new("PasswordResetTokens", LegacyDisposition.Excluded, "not migrated", string.Empty, "short-lived reset links", SecurityUpdate: true),
    ];

    /// <summary>Tables every supported legacy database has (the develop baseline).</summary>
    public static IEnumerable<LegacyTable> Baseline => All.Where(table => !table.SecurityUpdate && !table.Optional);
}
