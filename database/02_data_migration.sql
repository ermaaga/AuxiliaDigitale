-- ============================================================================
-- AUXILIA DATABASE REDESIGN - DATA MIGRATION SCRIPT (ETL: public -> v2)
-- File: sysadmin/postgres/script/02_data_migration.sql
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

\echo '>>> [1/11] Migrazione di system.languages...'
INSERT INTO system.languages (id, code, name, is_active, created_at)
SELECT "Id", "Code", "Name", "IsActive", clock_timestamp()
FROM public."Languages"
ON CONFLICT (id) DO UPDATE 
SET code = EXCLUDED.code, name = EXCLUDED.name, is_active = EXCLUDED.is_active;

-- Allinea sequence per languages
SELECT setval('system.languages_id_seq', COALESCE((SELECT MAX(id) FROM system.languages), 1));

\echo '>>> [2/11] Migrazione di system.resource_keys e system.resource_translations...'
INSERT INTO system.resource_keys (id, key_name, category)
SELECT "Id", "Key", "Category"
FROM public."ResourceKeys"
ON CONFLICT (id) DO UPDATE 
SET key_name = EXCLUDED.key_name, category = EXCLUDED.category;

SELECT setval('system.resource_keys_id_seq', COALESCE((SELECT MAX(id) FROM system.resource_keys), 1));

INSERT INTO system.resource_translations (id, resource_key_id, language_id, translation_value)
SELECT "Id", "ResourceKeyId", "LanguageId", "Value"
FROM public."ResourceTranslations"
ON CONFLICT (resource_key_id, language_id) DO UPDATE 
SET translation_value = EXCLUDED.translation_value;

SELECT setval('system.resource_translations_id_seq', COALESCE((SELECT MAX(id) FROM system.resource_translations), 1));

\echo '>>> [3/11] Migrazione di system.configurations e system.email_settings...'
INSERT INTO system.configurations (id, key, value, description, updated_at)
SELECT "Id", "Key", "Value", "Description", "UpdatedAt"
FROM public."SystemConfigurations"
ON CONFLICT (key) DO UPDATE 
SET value = EXCLUDED.value, description = EXCLUDED.description, updated_at = EXCLUDED.updated_at;

SELECT setval('system.configurations_id_seq', COALESCE((SELECT MAX(id) FROM system.configurations), 1));

INSERT INTO system.email_settings (id, smtp_server, smtp_port, enable_ssl, username, password_encrypted, from_email, from_name, is_active)
SELECT 
    1, 
    "SmtpServer", 
    "SmtpPort", 
    "EnableSsl", 
    "Username", 
    "Password", 
    "FromEmail", 
    "FromName", 
    "IsActive"
FROM public."EmailConfigurations"
LIMIT 1
ON CONFLICT (id) DO UPDATE 
SET smtp_server = EXCLUDED.smtp_server, smtp_port = EXCLUDED.smtp_port, username = EXCLUDED.username, from_email = EXCLUDED.from_email;

\echo '>>> [4/11] Migrazione di iam.roles e iam.specializations...'
INSERT INTO iam.roles (id, code, name, description)
SELECT 
    "Id", 
    CASE 
        WHEN "Name" = 'Administrator' THEN 'administrator'
        WHEN "Name" = 'Employee' THEN 'employee'
        WHEN "Name" = 'Client' THEN 'client'
        WHEN "Name" = 'SystemConfigurator' THEN 'system_configurator'
        ELSE lower("Name")
    END,
    "Name", 
    "Description"
FROM public."Roles"
ON CONFLICT (id) DO NOTHING;

SELECT setval('iam.roles_id_seq', COALESCE((SELECT MAX(id) FROM iam.roles), 1));

INSERT INTO iam.specializations (id, role_id, name, description, email, work_phone, has_private_dossiers, is_active, created_at)
SELECT 
    "Id", 
    "RoleId", 
    "Name", 
    "Description", 
    NULLIF("Email", ''), 
    NULLIF("WorkNumber", ''), 
    "PrivateSubscriptions", 
    "IsActive", 
    "CreatedAt"
FROM public."RoleSpecializations"
ON CONFLICT (id) DO NOTHING;

SELECT setval('iam.specializations_id_seq', COALESCE((SELECT MAX(id) FROM iam.specializations), 1));

\echo '>>> [5/11] Deduplicazione utenti e migrazione in iam.users...'

-- Tabella temporanea per rimappare gli ID duplicati
CREATE TEMP TABLE temp_user_remap AS
WITH ranked AS (
    SELECT 
        "Id" as old_id,
        "FiscalCode",
        CASE 
            WHEN "FiscalCode" IS NULL OR TRIM("FiscalCode") = '' THEN "Id"
            ELSE FIRST_VALUE("Id") OVER (
                PARTITION BY TRIM("FiscalCode")
                ORDER BY 
                    (CASE WHEN "Email" NOT IN ('/', '') THEN 1 ELSE 0 END) DESC,
                    (CASE WHEN "Username" NOT IN ('/', '') THEN 1 ELSE 0 END) DESC,
                    (CASE WHEN "IsActive" THEN 1 ELSE 0 END) DESC,
                    "CreatedAt" ASC
            )
        END as master_id
    FROM public."Users"
)
SELECT old_id, master_id FROM ranked;

-- Disambiguazione di eventuali Username duplicati tra persone diverse
CREATE TEMP TABLE temp_distinct_users AS
WITH master_users AS (
    SELECT DISTINCT u.*
    FROM public."Users" u
    JOIN temp_user_remap r ON u."Id" = r.master_id
),
cleaned_users AS (
    SELECT 
        u."Id",
        -- Se username è '/' o '' diventa NULL
        CASE 
            WHEN u."Username" IN ('/', '') THEN NULL
            ELSE u."Username"
        END as raw_username,
        NULLIF(u."PasswordHash", '') as password_hash,
        COALESCE(NULLIF(TRIM(u."FullName"), ''), 'N.D.') as first_name,
        COALESCE(NULLIF(TRIM(u."Surname"), ''), 'N.D.') as last_name,
        CASE 
            WHEN u."Email" IN ('/', '') THEN NULL
            ELSE u."Email"
        END as email,
        NULLIF(TRIM(u."Phone"), '') as phone,
        CASE 
            WHEN TRIM(u."FiscalCode") = 'tysonbvruchi@gmail.com' THEN NULL
            ELSE NULLIF(UPPER(TRIM(u."FiscalCode")), '')
        END as fiscal_code,
        CASE 
            WHEN u."DateOfBirth" = '-infinity'::timestamptz OR u."DateOfBirth" < '1900-01-01'::timestamptz THEN NULL
            ELSE u."DateOfBirth"::date
        END as date_of_birth,
        NULLIF(u."ProfileImage", '') as profile_image_path,
        CASE WHEN u."LanguageId" IN (1, 2) THEN u."LanguageId"::smallint ELSE 1::smallint END as preferred_language_id,
        u."PrivacyConsent" as privacy_consent,
        u."IsActive" as is_active,
        u."IsDefaultEmployee" as is_default_employee,
        u."EnableConfigurationSettings" as can_configure_system,
        COALESCE(u."CustomFields", '{}'::jsonb) as metadata,
        u."CreatedAt" as created_at,
        u."AssignedEmployeeId",
        u."AssignedAdministratorId"
    FROM master_users u
),
disambiguated_users AS (
    SELECT 
        c.*,
        -- Se due persone fisiche hanno lo stesso username (case-insensitive per citext), la seconda usa il codice fiscale o id
        CASE 
            WHEN c.raw_username IS NULL THEN NULL
            WHEN count(*) OVER (PARTITION BY lower(c.raw_username)) > 1 AND ROW_NUMBER() OVER (PARTITION BY lower(c.raw_username) ORDER BY c.created_at) > 1 
            THEN COALESCE(c.fiscal_code, c.raw_username || '_' || c."Id"::text)
            ELSE c.raw_username
        END as final_username
    FROM cleaned_users c
)
SELECT * FROM disambiguated_users;

-- Inserimento in iam.users
INSERT INTO iam.users (
    id, username, password_hash, first_name, last_name, email, phone, 
    fiscal_code, date_of_birth, profile_image_path, preferred_language_id, 
    privacy_consent, is_active, is_default_employee, can_configure_system, 
    metadata, created_at
)
SELECT 
    "Id", 
    final_username, 
    -- Se username è null, password_hash deve essere null (vincolo ck_users_login_creds)
    CASE WHEN final_username IS NULL THEN NULL ELSE password_hash END,
    first_name, 
    last_name, 
    email, 
    phone, 
    fiscal_code, 
    date_of_birth, 
    profile_image_path, 
    preferred_language_id, 
    privacy_consent, 
    is_active, 
    is_default_employee, 
    can_configure_system, 
    metadata, 
    created_at
FROM temp_distinct_users
ON CONFLICT (id) DO NOTHING;

SELECT setval('iam.users_id_seq', COALESCE((SELECT MAX(id) FROM iam.users), 1));

-- Migrazione delle relazioni utente: iam.user_roles
INSERT INTO iam.user_roles (user_id, role_id, assigned_at)
SELECT DISTINCT r.master_id, ur."RoleId"::smallint, clock_timestamp()
FROM public."UserRoles" ur
JOIN temp_user_remap r ON ur."UserId" = r.old_id
ON CONFLICT (user_id, role_id) DO NOTHING;

-- Migrazione delle specializzazioni utente: iam.user_specializations
INSERT INTO iam.user_specializations (user_id, specialization_id, assigned_at)
SELECT DISTINCT r.master_id, urs."RoleSpecializationId"::smallint, urs."AssignedAt"
FROM public."UserRoleSpecializations" urs
JOIN temp_user_remap r ON urs."UserId" = r.old_id
ON CONFLICT (user_id, specialization_id) DO NOTHING;

-- Migrazione delle assegnazioni: iam.user_assignments
INSERT INTO iam.user_assignments (client_id, assigned_to_user_id, assignment_role, assigned_at, is_active)
SELECT DISTINCT 
    r_client.master_id as client_id, 
    r_emp.master_id as assigned_to_user_id, 
    'Operator' as assignment_role, 
    u."CreatedAt", 
    true
FROM public."Users" u
JOIN temp_user_remap r_client ON u."Id" = r_client.old_id
JOIN temp_user_remap r_emp ON u."AssignedEmployeeId" = r_emp.old_id
WHERE u."AssignedEmployeeId" IS NOT NULL
ON CONFLICT (client_id, assigned_to_user_id, assignment_role) DO NOTHING;

INSERT INTO iam.user_assignments (client_id, assigned_to_user_id, assignment_role, assigned_at, is_active)
SELECT DISTINCT 
    r_emp.master_id as client_id, 
    r_admin.master_id as assigned_to_user_id, 
    'Supervisor' as assignment_role, 
    u."CreatedAt", 
    true
FROM public."Users" u
JOIN temp_user_remap r_emp ON u."Id" = r_emp.old_id
JOIN temp_user_remap r_admin ON u."AssignedAdministratorId" = r_admin.old_id
WHERE u."AssignedAdministratorId" IS NOT NULL
ON CONFLICT (client_id, assigned_to_user_id, assignment_role) DO NOTHING;

\echo '>>> [6/11] Migrazione di dossier.services e dossier.folder_templates...'
INSERT INTO dossier.services (
    id, specialization_id, code, name, description, 
    price, standard_validity_days, is_active, created_at
)
SELECT 
    "Id", 
    "RoleSpecializationId"::smallint, 
    'SERV_' || lpad("Id"::text, 4, '0') || '_' || lower(regexp_replace("Name", '[^a-zA-Z0-9]+', '_', 'g')), 
    "Name", 
    "Description", 
    "Price", 
    "DurationDays", 
    "IsActive", 
    clock_timestamp()
FROM public."Memberships"
ON CONFLICT (id) DO NOTHING;

SELECT setval('dossier.services_id_seq', COALESCE((SELECT MAX(id) FROM dossier.services), 1));

INSERT INTO dossier.folder_templates (id, service_id, parent_id, name, sort_order)
SELECT "Id", "MembershipId", "ParentId", "Name", "SortOrder"
FROM public."MembershipFolderTemplates"
ON CONFLICT (id) DO NOTHING;

SELECT setval('dossier.folder_templates_id_seq', COALESCE((SELECT MAX(id) FROM dossier.folder_templates), 1));

\echo '>>> [7/11] Migrazione di dossier.cases (ex Subscriptions)...'
INSERT INTO dossier.cases (
    id, case_number, client_id, service_id, specialization_id, 
    assigned_operator_id, status, amount_paid, start_date, end_date, 
    reference_year, notes, metadata, created_at
)
SELECT 
    s."Id",
    'PRAT-' || to_char(s."StartDate", 'YYYY') || '-' || lpad(s."Id"::text, 5, '0') as case_number,
    r.master_id as client_id,
    s."MembershipId" as service_id,
    s."RoleSpecializationId"::smallint as specialization_id,
    u."AssignedEmployeeId" as assigned_operator_id,
    CASE 
        WHEN s."IsRejected" THEN 'Rejected'
        WHEN s."Status" = 0 THEN 'Inserted'
        WHEN s."Status" = 1 THEN 'InProgress'
        WHEN s."Status" = 2 THEN 'Sent'
        WHEN s."Status" = 3 THEN 'Completed'
        ELSE 'Inserted'
    END as status,
    s."AmountPaid" as amount_paid,
    s."StartDate"::date as start_date,
    s."EndDate"::date as end_date,
    EXTRACT(YEAR FROM s."StartDate")::smallint as reference_year,
    NULL as notes,
    COALESCE(s."CustomFields", '{}'::jsonb) as metadata,
    s."StartDate" as created_at
FROM public."Subscriptions" s
JOIN temp_user_remap r ON s."UserId" = r.old_id
LEFT JOIN public."Users" u ON r.master_id = u."Id"
ON CONFLICT (id) DO NOTHING;

SELECT setval('dossier.cases_id_seq', COALESCE((SELECT MAX(id) FROM dossier.cases), 1));

\echo '>>> [8/11] Migrazione di dms.storage_providers, dms.categories e dms.documents...'
INSERT INTO dms.storage_providers (id, code, provider_type, base_endpoint, container_or_bucket, is_default, is_active)
VALUES 
    (1, 'azure_prod', 'AzureBlob', 'https://stauxiliafilesprod01.blob.core.windows.net', 'documents', true, true),
    (2, 'azure_demo', 'AzureBlob', 'https://stfilesdemo01.blob.core.windows.net', 'documents', false, true),
    (3, 'azure_qa', 'AzureBlob', 'https://stauxiliafilesqa01.blob.core.windows.net', 'documents', false, true)
ON CONFLICT (id) DO NOTHING;

SELECT setval('dms.storage_providers_id_seq', 3);

INSERT INTO dms.categories (id, code, name, description)
VALUES 
    (1, '730', 'Modello 730 / Redditi', 'Dichiarazioni dei redditi e documentazione fiscale 730'),
    (2, 'ISEE', 'ISEE / DSU', 'Dichiarazione Sostitutiva Unica e attestazioni ISEE'),
    (3, 'ADI', 'Assegno di Inclusione (ADI)', 'Domande e documentazione per Assegno di Inclusione'),
    (4, 'INVALIDITA_CIVILE', 'Invalidità Civile / L. 104', 'Pratiche mediche e istanze di invalidità civile e handicap'),
    (5, 'PENSIONI', 'Pensioni / Ricostituzioni', 'Pensioni di vecchiaia, reversibilità, ricostituzioni reddituali ed Ecocert'),
    (6, 'NASPI', 'NASpI / Disoccupazione', 'Indennità di disoccupazione e comunicazioni INPS'),
    (7, 'LOCAZIONI', 'Locazioni e Contratti', 'Registrazione contratti di locazione e rinnovi'),
    (8, 'ASSEGNO_UNICO', 'Assegno Unico Universale', 'Richieste e rinnovi Assegno Unico per figli'),
    (9, 'MATERNITA', 'Maternità e Congedi', 'Bonus bebè, congedi parentali e indennità di maternità'),
    (10, 'F24', 'Modello F24', 'Quietanza e modelli F24'),
    (11, 'GENERAL', 'Generale / Altro', 'Documenti di riconoscimento, contratti e altro')
ON CONFLICT (id) DO NOTHING;

SELECT setval('dms.categories_id_seq', 11);

INSERT INTO dms.documents (
    id, client_id, case_id, folder_template_id, category_id,
    file_name, file_extension, mime_type, file_size_bytes,
    storage_provider_id, storage_relative_path, reference_year,
    description, metadata, uploaded_by_user_id, uploaded_at
)
SELECT 
    d."Id",
    r_client.master_id as client_id,
    d."SubscriptionId" as case_id,
    d."FolderTemplateId" as folder_template_id,
    CASE 
        WHEN UPPER(d."Area") LIKE '%730%' THEN 1::smallint
        WHEN UPPER(d."Area") LIKE '%ISEE%' THEN 2::smallint
        WHEN UPPER(d."Area") LIKE '%ADI%' OR UPPER(d."Area") LIKE '%INCLUSIONE%' THEN 3::smallint
        WHEN UPPER(d."Area") LIKE '%INV%CIV%' OR UPPER(d."Area") LIKE '%INVALIDIT%' OR UPPER(d."Area") LIKE '%104%' THEN 4::smallint
        WHEN UPPER(d."Area") LIKE '%PENSION%' OR UPPER(d."Area") LIKE '%RICOSTITUZ%' OR UPPER(d."Area") LIKE '%ECOCERT%' OR UPPER(d."Area") LIKE '%SUPPLEMENTO%' THEN 5::smallint
        WHEN UPPER(d."Area") LIKE '%NASPI%' THEN 6::smallint
        WHEN UPPER(d."Area") LIKE '%LOCAZION%' THEN 7::smallint
        WHEN UPPER(d."Area") LIKE '%ASSEGNO%UNICO%' THEN 8::smallint
        WHEN UPPER(d."Area") LIKE '%MATERNIT%' OR UPPER(d."Area") LIKE '%CONGEDO%' OR UPPER(d."Area") LIKE '%BONUS%MAMMA%' OR UPPER(d."Area") LIKE '%BONUS%NATI%' THEN 9::smallint
        WHEN UPPER(d."Area") LIKE '%F24%' THEN 10::smallint
        ELSE 11::smallint
    END as category_id,
    d."FileName" as file_name,
    COALESCE(lower(substring(d."FileName" from '\.([^\.]+)$')), 'bin') as file_extension,
    CASE 
        WHEN d."FileType" IS NULL OR TRIM(d."FileType") = '' THEN 'application/pdf'
        ELSE d."FileType"
    END as mime_type,
    d."FileSize" as file_size_bytes,
    CASE 
        WHEN d."FilePath" LIKE '%stauxiliafilesprod01%' THEN 1::smallint
        WHEN d."FilePath" LIKE '%stfilesdemo01%' THEN 2::smallint
        WHEN d."FilePath" LIKE '%stauxiliafilesqa01%' THEN 3::smallint
        ELSE 1::smallint
    END as storage_provider_id,
    regexp_replace(d."FilePath", '^https://[^/]+/[^/]+/', '') as storage_relative_path,
    CASE WHEN d."ReferenceYear" > 1900 THEN d."ReferenceYear"::smallint ELSE NULL END as reference_year,
    d."Description" as description,
    COALESCE(d."CustomFields", '{}'::jsonb) as metadata,
    r_uploader.master_id as uploaded_by_user_id,
    d."UploadedAt" as uploaded_at
FROM public."UserDocuments" d
JOIN temp_user_remap r_client ON d."UserId" = r_client.old_id
JOIN temp_user_remap r_uploader ON d."UploadedByUserId" = r_uploader.old_id
ON CONFLICT (id) DO NOTHING;

SELECT setval('dms.documents_id_seq', COALESCE((SELECT MAX(id) FROM dms.documents), 1));

\echo '>>> [9/11] Migrazione di scheduling.appointments...'
INSERT INTO scheduling.appointments (
    id, client_id, operator_id, case_id, time_window, 
    status, notes, show_in_global_calendar, created_at
)
SELECT 
    a."Id",
    r_client.master_id as client_id,
    r_emp.master_id as operator_id,
    NULL as case_id,
    tstzrange(a."ScheduledDate", a."ScheduledDate" + (a."DurationMinutes" || ' minutes')::interval, '[)') as time_window,
    a."Status" as status,
    a."Notes" as notes,
    a."ShowInGlobalCalendare" as show_in_global_calendar,
    a."CreatedAt" as created_at
FROM public."Appointments" a
JOIN temp_user_remap r_client ON a."ClientId" = r_client.old_id
JOIN temp_user_remap r_emp ON a."EmployeeId" = r_emp.old_id
ON CONFLICT (id) DO NOTHING;

SELECT setval('scheduling.appointments_id_seq', COALESCE((SELECT MAX(id) FROM scheduling.appointments), 1));

\echo '>>> [10/11] Migrazione e unificazione di viste e permessi UI (system.ui_views e system.ui_role_permissions)...'

-- Inserimento delle viste distinte
INSERT INTO system.ui_views (code, route_path, display_name_key, sort_order)
VALUES 
    ('Dashboard', '/dashboard', 'Menu.Dashboard', 1),
    ('Clients', '/clients', 'Menu.Clients', 2),
    ('AllClients', '/all-clients', 'Menu.AllClients', 3),
    ('Employees', '/employees', 'Menu.Employees', 4),
    ('Documents', '/documents', 'Menu.Documents', 5),
    ('Appointments', '/appointments', 'Menu.Appointments', 6),
    ('Subscriptions', '/subscriptions', 'Menu.Subscriptions', 7),
    ('Memberships', '/memberships', 'Menu.Memberships', 8),
    ('Requests', '/requests', 'Menu.Requests', 9),
    ('Requests.UserRequests', '/requests/users', 'Menu.UserRequests', 10),
    ('Requests.RegistrationRequests', '/requests/registrations', 'Menu.RegistrationRequests', 11),
    ('RegistrationRequests', '/registration-requests', 'Menu.RegistrationRequests', 12),
    ('Logs', '/logs', 'Menu.Logs', 13),
    ('Sessions', '/sessions', 'Menu.Sessions', 14)
ON CONFLICT (code) DO NOTHING;

-- Inserimento dei permessi e griglie associati
INSERT INTO system.ui_role_permissions (role_id, view_id, is_enabled, grid_configuration, updated_at)
SELECT 
    r.id as role_id,
    v.id as view_id,
    pc."IsEnabled" as is_enabled,
    pc."ConfigurationGrid" as grid_configuration,
    COALESCE(pc."UpdatedAt", pc."CreatedAt") as updated_at
FROM public."PageConfigurations" pc
JOIN iam.roles r ON lower(r.name) = lower(pc."Role")
JOIN system.ui_views v ON v.code = pc."PageName"
ON CONFLICT (role_id, view_id) DO UPDATE 
SET grid_configuration = EXCLUDED.grid_configuration, is_enabled = EXCLUDED.is_enabled;

\echo '>>> [11/11] Migrazione di system.app_logs e system.notifications...'
INSERT INTO system.app_logs (id, log_level, message, stack_trace, source_context, user_id, ip_address, created_at)
SELECT 
    l."Id",
    l."Level",
    l."Message",
    l."StackTrace",
    l."Source",
    r.master_id as user_id,
    NULLIF(TRIM(l."IpAddress"), '')::inet,
    l."CreatedAt"
FROM public."AppLogs" l
LEFT JOIN temp_user_remap r ON l."UserId" = r.old_id
ON CONFLICT (id) DO NOTHING;

SELECT setval('system.app_logs_id_seq', COALESCE((SELECT MAX(id) FROM system.app_logs), 1));

INSERT INTO system.notifications (
    id, user_id, title, message, type, is_read, 
    related_entity_type, related_entity_id, is_created_by_final_user, created_at
)
SELECT 
    n."Id",
    r.master_id as user_id,
    n."Title",
    n."Message",
    n."Type",
    n."IsRead",
    'Appointment' as related_entity_type,
    n."RelatedEntityId" as related_entity_id,
    n."IsCreatedByFinalUser",
    n."CreatedAt"
FROM public."Notifications" n
JOIN temp_user_remap r ON n."UserId" = r.old_id
ON CONFLICT (id) DO NOTHING;

SELECT setval('system.notifications_id_seq', COALESCE((SELECT MAX(id) FROM system.notifications), 1));

COMMIT;

\echo '======================================================='
\echo '>>> MIGRAZIONE DATI COMPLETATA CON SUCCESSO! <<<'
\echo '======================================================='
