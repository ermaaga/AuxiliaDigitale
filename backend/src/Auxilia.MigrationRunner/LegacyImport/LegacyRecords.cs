namespace Auxilia.MigrationRunner.LegacyImport;

// Read model of the legacy database (baseline D-30: develop @ 8fa6622 + Security_Update), one class per table that the
// import reads, with the legacy column names and types. Timestamps are `timestamp with time zone` (UTC); jsonb columns
// are read as text. Tables that are not migrated (WorkoutPlans, UserSessions, AppLogs, PasswordResetTokens) are only
// counted (LegacyInventory) and have no class here. Mapping: docs/migration/mapping.md.

internal sealed class LegacyUser
{
    public int Id { get; init; }

    public required string Username { get; init; }

    public required string PasswordHash { get; init; }

    public required string FullName { get; init; }

    public required string Surname { get; init; }

    public required string Email { get; init; }

    public string? Phone { get; init; }

    public string? FiscalCode { get; init; }

    public DateTime DateOfBirth { get; init; }

    public bool IsActive { get; init; }

    public bool IsDefaultEmployee { get; init; }

    public bool EnableConfigurationSettings { get; init; }

    public bool PrivacyConsent { get; init; }

    public int LanguageId { get; init; }

    public int? AssignedEmployeeId { get; init; }

    public int? AssignedAdministratorId { get; init; }

    /// <summary>Data URL (<c>data:image/jpeg;base64,…</c>) set by the profile page.</summary>
    public string? ProfileImage { get; init; }

    public string? CustomFields { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>Only with the Security_Update schema (<see cref="LegacySchema.HasSecurityUpdate"/>).</summary>
    public DateTime? PasswordChangedAt { get; init; }
}

internal sealed class LegacyRole
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }
}

internal sealed class LegacyUserRole
{
    public int UserId { get; init; }

    public int RoleId { get; init; }
}

internal sealed class LegacyRoleSpecialization
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Email { get; init; }

    public required string WorkNumber { get; init; }

    public int RoleId { get; init; }

    public bool PrivateSubscriptions { get; init; }

    public bool IsActive { get; init; }

    public DateTime CreatedAt { get; init; }
}

internal sealed class LegacyUserRoleSpecialization
{
    public int UserId { get; init; }

    public int RoleSpecializationId { get; init; }

    public DateTime AssignedAt { get; init; }
}

internal sealed class LegacyMembershipType
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public bool IsActive { get; init; }
}

internal sealed class LegacyMembership
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public decimal Price { get; init; }

    public int DurationDays { get; init; }

    public bool IsActive { get; init; }

    public int? MembershipTypeId { get; init; }

    public int? RoleSpecializationId { get; init; }
}

internal sealed class LegacyMembershipFolderTemplate
{
    public int Id { get; init; }

    public int MembershipId { get; init; }

    public required string Name { get; init; }

    public int? ParentId { get; init; }

    public int SortOrder { get; init; }
}

internal sealed class LegacySubscription
{
    public int Id { get; init; }

    public int UserId { get; init; }

    public int MembershipId { get; init; }

    public int? RoleSpecializationId { get; init; }

    public DateTime StartDate { get; init; }

    public DateTime? EndDate { get; init; }

    /// <summary><c>SubscriptionStatus</c>: 0 Inserted, 1 InProgress, 2 Sent, 3 Completed.</summary>
    public int Status { get; init; }

    public bool IsActive { get; init; }

    public bool IsRejected { get; init; }

    public decimal AmountPaid { get; init; }

    public string? CustomFields { get; init; }
}

internal sealed class LegacyUserDocument
{
    public int Id { get; init; }

    public int UserId { get; init; }

    public int UploadedByUserId { get; init; }

    public int? SubscriptionId { get; init; }

    public int? FolderTemplateId { get; init; }

    public required string FileName { get; init; }

    /// <summary>Storage path of the file (<c>queued</c> while the legacy upload queue had not stored it yet).</summary>
    public required string FilePath { get; init; }

    public required string FileType { get; init; }

    public long FileSize { get; init; }

    public required string Area { get; init; }

    public int ReferenceYear { get; init; }

    public string? Description { get; init; }

    public DateTime UploadedAt { get; init; }

    public string? CustomFields { get; init; }
}

internal sealed class LegacyAppointment
{
    public int Id { get; init; }

    public int ClientId { get; init; }

    public int EmployeeId { get; init; }

    public DateTime ScheduledDate { get; init; }

    public int DurationMinutes { get; init; }

    public required string Status { get; init; }

    public string? Notes { get; init; }

    public bool ShowInGlobalCalendare { get; init; }

    public DateTime CreatedAt { get; init; }

    public string? CustomFields { get; init; }
}

internal sealed class LegacyRequest
{
    public int Id { get; init; }

    public int SenderId { get; init; }

    /// <summary><c>null</c> = the office (the Administrators).</summary>
    public int? ReceiverId { get; init; }

    public required string Subject { get; init; }

    public required string Message { get; init; }

    public string? Response { get; init; }

    public required string Type { get; init; }

    public required string Status { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? RespondedAt { get; init; }
}

internal sealed class LegacyNotification
{
    public int Id { get; init; }

    public int UserId { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public required string Type { get; init; }

    public int? RelatedEntityId { get; init; }

    public bool IsRead { get; init; }

    public bool IsCreatedByFinalUser { get; init; }

    public DateTime CreatedAt { get; init; }
}

internal sealed class LegacyRegistrationRequest
{
    public int Id { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public required string Phone { get; init; }

    public required string FiscalCode { get; init; }

    public DateTime DateOfBirth { get; init; }

    public string? Notes { get; init; }

    public bool IsProcessed { get; init; }

    public int? ProcessedByUserId { get; init; }

    public DateTime? ProcessedDate { get; init; }

    public DateTime RequestDate { get; init; }
}

internal sealed class LegacyImportType
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string TargetEntity { get; init; }

    public int CreatedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }
}

internal sealed class LegacyImport
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public required string FileName { get; init; }

    public int ImportTypeId { get; init; }

    public required string Status { get; init; }

    public int Progress { get; init; }

    public string? ErrorMessage { get; init; }

    public int CreatedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? CompletedAt { get; init; }
}

internal sealed class LegacyImportJob
{
    public int Id { get; init; }

    public required string FileName { get; init; }

    public int ImportTypeId { get; init; }

    public required string Status { get; init; }

    public int TotalRows { get; init; }

    public int ProcessedRows { get; init; }

    public int SuccessRows { get; init; }

    public int FailedRows { get; init; }

    public string? ErrorMessage { get; init; }

    public int CreatedByUserId { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }
}

internal sealed class LegacyLanguage
{
    public int Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public bool IsActive { get; init; }
}

internal sealed class LegacyResourceKey
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Category { get; init; }
}

internal sealed class LegacyResourceTranslation
{
    public int Id { get; init; }

    public int ResourceKeyId { get; init; }

    public int LanguageId { get; init; }

    public required string Value { get; init; }
}

internal sealed class LegacySystemConfiguration
{
    public int Id { get; init; }

    public required string Key { get; init; }

    public required string Value { get; init; }

    public string? Description { get; init; }

    public DateTime UpdatedAt { get; init; }
}

/// <summary>The legacy SMTP account; the password is plain text in the legacy database and is re-encrypted (never logged).</summary>
internal sealed class LegacyEmailConfiguration
{
    public int Id { get; init; }

    public required string SmtpServer { get; init; }

    public int SmtpPort { get; init; }

    public bool EnableSsl { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string FromEmail { get; init; }

    public required string FromName { get; init; }

    public bool IsActive { get; init; }
}

internal sealed class LegacyModuleConfiguration
{
    public int Id { get; init; }

    public required string ModulePath { get; init; }

    public required string Role { get; init; }

    public bool IsEnabled { get; init; }
}

internal sealed class LegacyPageConfiguration
{
    public int Id { get; init; }

    public required string PageName { get; init; }

    public required string Role { get; init; }

    public bool IsEnabled { get; init; }

    public int? ParentPageId { get; init; }

    public string? ConfigurationGrid { get; init; }
}

internal sealed class LegacyEntityConfiguration
{
    public int Id { get; init; }

    public required string EntityName { get; init; }

    public required string Configuration { get; init; }
}

/// <summary>Security_Update only.</summary>
internal sealed class LegacyLoginAuditLog
{
    public int Id { get; init; }

    public int? UserId { get; init; }

    public required string Username { get; init; }

    public bool Success { get; init; }

    public string? FailureReason { get; init; }

    public required string LoginType { get; init; }

    public string? IpAddress { get; init; }

    public DateTime LoginAt { get; init; }
}

/// <summary>Security_Update only.</summary>
internal sealed class LegacyPasswordHistory
{
    public int Id { get; init; }

    public int UserId { get; init; }

    public required string PasswordHash { get; init; }

    public DateTime CreatedAt { get; init; }
}
