-- E-05 configuration rows on top of seed.sql (languages 1 en, 2 it). Fake SMTP credentials.
INSERT INTO "ResourceKeys" ("Id", "Key", "Category") VALUES (1, 'Save', 'Common'), (2, 'TenantOnly', 'Custom'), (3, 'WorkoutPlans', 'Menu');
INSERT INTO "ResourceTranslations" ("ResourceKeyId", "LanguageId", "Value") VALUES
    (1, 2, 'Salva ora'), (1, 1, 'Save'), (2, 2, 'Solo nostro'), (3, 2, 'Schede');
INSERT INTO "SystemConfigurations" ("Key", "Value", "Description", "UpdatedAt") VALUES
    ('RegistrationEnabled', 'True', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('SubscriptionExpiringDays', '15', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('AutoSubscriptionExpiry', 'not a bool', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('UseAppName', 'False', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('ThemeType', 'gradient', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('ThemePrimary', '#112233', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('ThemeSecondary', '#445566', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('BackgroundType', 'image', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('BackgroundGradient', 'linear-gradient(135deg, #abcdef 0%, #fedcba 100%)', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('BackgroundImage', '/9j/4AAQSkZJRg==', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('DefaultPassword', 'password', NULL, TIMESTAMPTZ '2025-01-01Z'),
    ('Mystery', 'x', NULL, TIMESTAMPTZ '2025-01-01Z');
INSERT INTO "EmailConfigurations" ("Id", "SmtpServer", "SmtpPort", "EnableSsl", "Username", "Password", "FromEmail", "FromName", "IsActive") VALUES
    (1, 'smtp.example.test', 587, TRUE, 'mailer', 'not-a-real-secret', 'noreply@example.test', 'Studio', TRUE);
INSERT INTO "ModuleConfigurations" ("Role", "ModulePath", "IsEnabled", "CreatedAt") VALUES
    ('Employee', 'Subscriptions', TRUE, TIMESTAMPTZ '2025-01-01Z'), ('SystemConfigurator', 'Settings', TRUE, TIMESTAMPTZ '2025-01-01Z');
INSERT INTO "PageConfigurations" ("PageName", "Role", "IsEnabled", "CreatedAt", "ConfigurationGrid") VALUES
    ('Requests', 'Employee', FALSE, TIMESTAMPTZ '2025-01-01Z', NULL),
    ('Logs', 'Administrator', FALSE, TIMESTAMPTZ '2025-01-01Z', NULL),
    ('Subscriptions', 'Administrator', TRUE, TIMESTAMPTZ '2025-01-01Z',
        '[{"Label":"Membership","Property":"Membership.Name","FilterThisColumn":true,"OrderThisColumn":true},{"Label":"Client","Property":"User.FullName"},{"Label":"Unknown","Property":"Nope"}]'),
    ('Mystery', 'Administrator', FALSE, TIMESTAMPTZ '2025-01-01Z', NULL);
INSERT INTO "EntityConfigurations" ("EntityName", "Configuration", "CreatedAt") VALUES
    ('User', '[{"PropertyName":"CAF","PropertyType":"boolean","VisibleOnGrid":true},{"PropertyName":"Note","PropertyType":"text","VisibleOnGrid":false},{"PropertyName":"bad key","PropertyType":"text"}]', TIMESTAMPTZ '2025-01-01Z'),
    ('WorkoutPlan', '[]', TIMESTAMPTZ '2025-01-01Z');
