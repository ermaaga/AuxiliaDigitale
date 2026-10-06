-- E-03 rows on top of seed.sql and users.sql (user 3 is the client, specialization 1 the Employee one).
INSERT INTO "MembershipTypes" ("Id", "Name", "Description", "IsActive") VALUES (1, 'Fiscale', '', TRUE), (2, 'Fiscale', 'Duplicate', TRUE);
INSERT INTO "Memberships" ("Id", "Name", "Description", "Price", "DurationDays", "IsActive", "MembershipTypeId", "RoleSpecializationId") VALUES
    (1, '730', 'Dichiarazione dei redditi', 150.00, 365, TRUE, 1, 1),
    (2, 'ISEE', '', 0, 0, TRUE, NULL, NULL);
INSERT INTO "MembershipFolderTemplates" ("Id", "MembershipId", "Name", "ParentId", "SortOrder") VALUES
    (2, 1, 'Redditi', 1, 1), (1, 1, 'Documenti', NULL, 1), (3, 2, 'Orfana', 99, 1);
INSERT INTO "Subscriptions" ("Id", "UserId", "MembershipId", "RoleSpecializationId", "StartDate", "EndDate", "Status", "IsActive", "IsRejected", "AmountPaid") VALUES
    (1, 3, 1, NULL, TIMESTAMPTZ '2025-03-01 23:00:00Z', NULL, 1, TRUE, FALSE, 150.00),
    (2, 3, 2, NULL, TIMESTAMPTZ '2024-06-10 22:00:00Z', TIMESTAMPTZ '2024-07-01 10:00:00Z', 3, FALSE, TRUE, 0),
    (3, 1, 1, NULL, TIMESTAMPTZ '2025-01-01 00:00:00Z', NULL, 0, TRUE, FALSE, 0),
    (4, 3, 1, NULL, TIMESTAMPTZ '2025-01-01 00:00:00Z', NULL, 7, TRUE, FALSE, 0);
