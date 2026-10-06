-- E-02 rows on top of seed.sql (Security_Update baseline). Passwords are set by the test (BCrypt at run time).
UPDATE "Users" SET "Username" = 'mario rossi', "Phone" = 'not a phone', "FiscalCode" = 'vrdmra80e10h501z',
    "DateOfBirth" = TIMESTAMPTZ '1980-05-09 22:00:00Z', "PrivacyConsent" = TRUE, "ProfileImage" = 'data:image/png;base64,@@@'
    WHERE "Id" = 3;
UPDATE "Users" SET "AssignedAdministratorId" = 1 WHERE "Id" = 2;
UPDATE "Users" SET "PasswordChangedAt" = TIMESTAMPTZ '2025-04-01 08:00:00Z' WHERE "Id" = 1;
INSERT INTO "RoleSpecializations" ("Id", "Name", "Description", "Email", "WorkNumber", "RoleId", "PrivateSubscriptions", "IsActive", "CreatedAt") VALUES
    (1, 'Fiscale', 'Pratiche fiscali', 'not-an-email', '', 2, TRUE, TRUE, TIMESTAMPTZ '2025-01-05Z'),
    (2, 'Direzione', '', '', '', 1, FALSE, TRUE, TIMESTAMPTZ '2025-01-05Z');
INSERT INTO "UserRoleSpecializations" ("UserId", "RoleSpecializationId", "AssignedAt") VALUES
    (2, 1, TIMESTAMPTZ '2025-01-11Z'), (3, 1, TIMESTAMPTZ '2025-02-02Z');
INSERT INTO "PasswordHistories" ("UserId", "PasswordHash", "CreatedAt") VALUES (3, 'older-hash', TIMESTAMPTZ '2024-12-01Z');
INSERT INTO "LoginAuditLogs" ("UserId", "Username", "Success", "FailureReason", "LoginType", "IpAddress", "LoginAt") VALUES
    (1, 'admin', TRUE, NULL, 'Password', '10.0.0.1', TIMESTAMPTZ '2025-06-01Z'),
    (NULL, 'ghost', FALSE, 'InvalidOtp', 'Otp', NULL, TIMESTAMPTZ '2025-06-02Z'),
    (NULL, 'other', FALSE, NULL, 'Sso', NULL, TIMESTAMPTZ '2025-06-03Z');
