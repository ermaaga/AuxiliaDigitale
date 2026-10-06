-- E-04 rows on top of seed.sql, users.sql and cases.sql (user 3 is the client, case 1 is of service 1 with folder 2).
INSERT INTO "UserDocuments" ("Id", "UserId", "UploadedByUserId", "SubscriptionId", "FolderTemplateId", "FileName", "FilePath", "FileType",
                             "FileSize", "Area", "ReferenceYear", "UploadedAt", "Description") VALUES
    (1, 3, 2, 1, 2, '0b6f1c2e-3f4a-4d5b-8c9d-0e1f2a3b4c5d_730.pdf', 'uploads/documents/0b6f1c2e-3f4a-4d5b-8c9d-0e1f2a3b4c5d_730.pdf',
        'application/pdf', 8, ' Fiscale   2025 ', 2025, TIMESTAMPTZ '2025-03-05 09:00:00Z', 'Modello 730'),
    (2, 3, 3, NULL, NULL, 'carta.jpg', 'https://account.blob.core.windows.net/documents/1c7a2d3e-4f5a-4b6c-9d8e-1f2a3b4c5d6e_carta.jpg',
        'image/jpeg', 4, '', 0, TIMESTAMPTZ '2024-12-31 23:30:00Z', NULL),
    (3, 3, 2, NULL, NULL, 'missing.pdf', 'uploads/documents/missing.pdf', 'application/pdf', 1, '', 2025, TIMESTAMPTZ '2025-01-01Z', NULL),
    (4, 3, 2, NULL, NULL, 'queued.pdf', 'queued', 'application/pdf', 1, '', 2025, TIMESTAMPTZ '2025-01-01Z', NULL),
    (5, 1, 1, NULL, NULL, 'admin.pdf', 'uploads/documents/admin.pdf', 'application/pdf', 8, '', 2025, TIMESTAMPTZ '2025-01-01Z', NULL),
    (6, 3, 2, NULL, NULL, 'note.txt', 'uploads/documents/note.txt', 'text/plain', 5, '', 2025, TIMESTAMPTZ '2025-01-01Z', NULL);
