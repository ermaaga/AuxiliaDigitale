-- A few rows for the inventory tests (fake data; works on both baselines).
INSERT INTO "Languages" ("Id", "Code", "Name", "IsActive") VALUES (1, 'en', 'English', TRUE), (2, 'it', 'Italiano', TRUE);
INSERT INTO "Roles" ("Id", "Name", "Description") VALUES
    (1, 'Administrator', ''), (2, 'Employee', ''), (3, 'Client', ''), (4, 'SystemConfigurator', '');
INSERT INTO "Users" ("Id", "Username", "PasswordHash", "FullName", "Surname", "Email", "CreatedAt", "IsActive",
                     "IsDefaultEmployee", "EnableConfigurationSettings", "LanguageId", "AssignedEmployeeId") VALUES
    (1, 'admin', 'not-a-hash', 'Anna', 'Bianchi', 'admin@example.test', TIMESTAMPTZ '2025-01-10 09:00:00Z', TRUE, FALSE, TRUE, 2, NULL),
    (2, 'operatore', 'not-a-hash', 'Sara', 'Rossi', 'sara@example.test', TIMESTAMPTZ '2025-01-10 09:00:00Z', TRUE, TRUE, FALSE, 2, NULL),
    (3, 'cliente', 'not-a-hash', 'Mario', 'Verdi', 'mario@example.test', TIMESTAMPTZ '2025-02-01 10:00:00Z', TRUE, FALSE, FALSE, 2, 2),
    (4, 'system', 'not-a-hash', 'System', 'Configurator', 'system@example.test', TIMESTAMPTZ '2025-01-10 09:00:00Z', TRUE, FALSE, TRUE, 2, NULL),
    (5, 'orfano', 'not-a-hash', 'Luca', 'Neri', 'luca@example.test', TIMESTAMPTZ '2025-03-01 10:00:00Z', FALSE, FALSE, FALSE, 1, NULL);
INSERT INTO "UserRoles" ("UserId", "RoleId") VALUES (1, 1), (2, 2), (3, 3), (4, 4);
INSERT INTO "WorkoutPlans" ("ClientId", "CreatedByEmployeeId", "Name", "Description", "Exercises", "StartDate", "EndDate",
                            "IsActive", "CreatedAt") VALUES
    (3, 2, 'Plan', '', '[]', TIMESTAMPTZ '2025-02-01Z', TIMESTAMPTZ '2025-03-01Z', TRUE, TIMESTAMPTZ '2025-02-01Z');
