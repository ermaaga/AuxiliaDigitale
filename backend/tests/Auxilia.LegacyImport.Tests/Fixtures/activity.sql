-- E-05 rows on top of seed.sql, users.sql and cases.sql (user 1 admin, 2 employee, 3 client; case 1 of client 3).
INSERT INTO "Appointments" ("Id", "ClientId", "EmployeeId", "ScheduledDate", "DurationMinutes", "Status", "Notes", "CreatedAt", "ShowInGlobalCalendare") VALUES
    (1, 3, 2, TIMESTAMPTZ '2025-04-10 08:30:00Z', 45, 'Completed', 'Consegna documenti', TIMESTAMPTZ '2025-04-01 10:00:00Z', TRUE),
    (2, 3, 2, TIMESTAMPTZ '2027-01-15 09:00:00Z', 0, 'Pending', NULL, TIMESTAMPTZ '2026-09-01 10:00:00Z', FALSE),
    (3, 3, 2, TIMESTAMPTZ '2025-05-10 08:30:00Z', 30, 'Postponed', NULL, TIMESTAMPTZ '2025-05-01 10:00:00Z', FALSE),
    (4, 1, 2, TIMESTAMPTZ '2025-05-10 08:30:00Z', 30, 'Approved', NULL, TIMESTAMPTZ '2025-05-01 10:00:00Z', FALSE);
INSERT INTO "Requests" ("Id", "SenderId", "ReceiverId", "Type", "Subject", "Message", "Status", "Response", "CreatedAt", "RespondedAt") VALUES
    (1, 3, NULL, 'Information', 'Orari', 'Quando siete aperti?', 'Responded', 'Dal lunedì al venerdì', TIMESTAMPTZ '2025-03-01 09:00:00Z', TIMESTAMPTZ '2025-03-02 09:00:00Z'),
    (2, 3, 2, 'Other', 'Pratica', 'Novità sulla pratica?', 'Pending', NULL, TIMESTAMPTZ '2025-03-03 09:00:00Z', NULL),
    (3, 4, NULL, 'General', 'System', 'Not migrated sender', 'Pending', NULL, TIMESTAMPTZ '2025-03-03 09:00:00Z', NULL);
INSERT INTO "Notifications" ("Id", "UserId", "Title", "Message", "Type", "IsRead", "CreatedAt", "RelatedEntityId") VALUES
    (1, 3, 'Appuntamento', 'Il tuo appuntamento è confermato', 'Appointment', TRUE, TIMESTAMPTZ '2025-04-02 10:00:00Z', 1),
    (2, 3, 'Pratica', 'La pratica è in lavorazione', 'Subscription', FALSE, TIMESTAMPTZ '2025-04-03 10:00:00Z', 1),
    (3, 4, 'System', 'For the system user', 'Info', FALSE, TIMESTAMPTZ '2025-04-03 10:00:00Z', NULL);
INSERT INTO "RegistrationRequests" ("Id", "FirstName", "LastName", "Email", "Phone", "FiscalCode", "DateOfBirth", "RequestDate", "IsProcessed", "ProcessedByUserId", "ProcessedDate", "Notes") VALUES
    (1, 'Mario', 'Verdi', 'MARIO@example.test', '3331234567', 'VRDMRA80E10H501Z', TIMESTAMPTZ '1980-05-09 22:00:00Z', TIMESTAMPTZ '2025-01-20Z', TRUE, 1, TIMESTAMPTZ '2025-01-21Z', 'ok'),
    (2, 'Gino', 'Neri', 'gino@example.test', '3331234568', 'NREGNI80E10H501Z', TIMESTAMPTZ '1980-05-09 22:00:00Z', TIMESTAMPTZ '2025-01-22Z', TRUE, 1, TIMESTAMPTZ '2025-01-23Z', 'no'),
    (3, 'Lia', 'Rossi', 'lia@example.test', '3331234569', 'RSSLIA80E50H501Z', TIMESTAMPTZ '1980-05-09 22:00:00Z', TIMESTAMPTZ '2025-02-01Z', FALSE, NULL, NULL, NULL),
    (4, 'Lia', 'Rossi', 'LIA@example.test', '3331234569', 'RSSLIA80E50H501Z', TIMESTAMPTZ '1980-05-09 22:00:00Z', TIMESTAMPTZ '2025-02-02Z', FALSE, NULL, NULL, NULL);
INSERT INTO "ImportTypes" ("Id", "Name", "TargetEntity", "CreatedAt", "CreatedByUserId") VALUES
    (1, 'Pratiche', 'Subscription', TIMESTAMPTZ '2025-01-01Z', 1), (2, 'Pratiche', 'Client', TIMESTAMPTZ '2025-01-01Z', 1);
INSERT INTO "Imports" ("Id", "Name", "ImportTypeId", "FileName", "Status", "Progress", "ErrorMessage", "ImportedData", "CreatedAt", "CompletedAt", "CreatedByUserId") VALUES
    (1, 'Gennaio', 1, 'pratiche.xlsx', 'Concluded', 100, NULL, '[{"a":1}]', TIMESTAMPTZ '2025-01-05Z', TIMESTAMPTZ '2025-01-05 01:00:00Z', 1),
    (2, '', 2, 'clienti.xlsx', 'Running', 40, NULL, NULL, TIMESTAMPTZ '2025-01-06Z', NULL, 1);
