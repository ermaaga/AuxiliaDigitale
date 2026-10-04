# Auxilia Database Redesign (PostgreSQL Architecture Blueprint)

> **Technical Data Architecture Document**  
> **Author:** Senior PostgreSQL DBA & Data Architect  
> **Target Engine:** PostgreSQL 18+  
> **Status:** Redesign Proposal & Migration Plan  
> **Date:** September 2026  

---

## Table of Contents
1. [Introduction and Objectives](#1-introduction-and-objectives)
2. [Critical Analysis of Current Schema (Why Change)](#2-critical-analysis-of-current-schema-why-change)
3. [Domain Realignment: From Gym to Tax Assistance Center (CAF/Patronato)](#3-domain-realignment-from-gym-to-tax-assistance-center-cafpatronato)
4. [New Domain Schema Architecture](#4-new-domain-schema-architecture)
5. [Entity Details and Architectural Decisions](#5-entity-details-and-architectural-decisions)
6. [Security and Credential Protection](#6-security-and-credential-protection)
7. [Data Normalization and Deduplication Strategy](#7-data-normalization-and-deduplication-strategy)
8. [Mapping Matrix (Old Schema → New Schema)](#8-mapping-matrix-old-schema--new-schema)

---

## 1. Introduction and Objectives

The `auxilia` database was originally generated using a **Code-First approach via Entity Framework Core**, inheriting conventions typical of SQL Server and assigning a purely passive role to the PostgreSQL relational engine.

The objectives of this redesign are:
1. **Ensure data integrity at the ACID level:** Do not delegate consistency and uniqueness solely to the C# application layer.
2. **Eliminate existing anomalies and duplicates:** Remediate the 44 duplicate tax code pairs, ghost accounts (`/`), and 94 chaotic variations of document folders/areas.
3. **Adopt native PostgreSQL best practices:** Dedicated logical schemas, standard `snake_case` naming conventions, optimal data types (`citext`, `inet`, `tstzrange`, `numeric(10,2)`, `date`), GIST exclusion constraints against overlapping, and automated audit triggers.
4. **Close critical security gaps:** Eliminate plaintext SMTP passwords and strongly type roles and permissions.

---

## 2. Critical Analysis of Current Schema (Why Change)

The audit of the production database revealed serious anomalies:

### 2.1. Absence of Uniqueness Constraints and Data Corruption
- **Fiscal Code (Codice Fiscale):** The `Users` table stored `FiscalCode` as plain text without a `UNIQUE` constraint or a dedicated index. This resulted in **44 pairs of duplicate users (88 records)** for the same individual (e.g., one record created during a Form 730 import and another created during an ISEE import).
- **Username and Email with Dummy Characters:** Due to strict non-null constraints in C#, **137 users were created with `Username = '/'`** and **135 with `Email = '/'`**. In a relational database, if a user is a citizen managed at the service desk without a web account, credentials must be `NULL`.
- **Unprotected Translations:** The `ResourceTranslations` table lacked a `UNIQUE(ResourceKeyId, LanguageId)` constraint, leaving it vulnerable to conflicting translations for the same key.

### 2.2. Missing Foreign Keys
In PostgreSQL, foreign keys are not automatically created unless explicitly configured in the DbContext:
- `Users.LanguageId` did not reference `Languages(Id)`.
- `Imports.ImportTypeId` did not reference `ImportTypes(Id)`.
- `WorkoutPlans.CreatedByEmployeeId` did not reference `Users(Id)`.
- `AppLogs.UserId` did not reference `Users(Id)`.
- `Notifications.RelatedEntityId` was an untyped integer (no referential integrity).

### 2.3. "Dead Columns" Anti-Pattern from `BaseEntity`
Every C# entity inheriting from `BaseEntity` generated two columns:
- `RowVersion bytea`: In PostgreSQL, EF Core does not automatically generate binary concurrency tokens without explicit configuration. As a result: **100% of records across all tables have `RowVersion = NULL`**.
- `CustomFields jsonb`: Inherited across 16 tables (including configuration tables, languages, and translations), but only ever populated in the `Users` table.

### 2.4. Incorrect Data Types
- **Dates of Birth:** `DateOfBirth` was stored as `timestamptz` with a `-infinity` default. A date of birth is a calendar value without a time or timezone; it must use the `date` type. Using `timestamptz` causes one-day off-by-one shifts due to UTC vs. local timezone offsets.
- **Currencies:** Prices and amounts (`Price`, `AmountPaid`) were untyped generic `numeric` columns instead of the canonical `numeric(10,2)`.
- **IP Addresses:** Stored as `text` instead of `inet`, preventing subnet queries and format validation.

### 2.5. Overlapping Appointments
The `Appointments` table used separate date and duration (in minutes) fields. Availability checking was delegated to a C# query, making it susceptible to race conditions during concurrent bookings. In PostgreSQL, using a range type (`tstzrange`) combined with an `EXCLUDE USING gist` constraint makes overlapping physically impossible at the transactional engine level.

---

## 3. Domain Realignment: From Gym to Tax Assistance Center (CAF/Patronato)

The application retains obvious remnants of gym / personal trainer management software:
- `WorkoutPlans` table (workout routines and exercises, 0 rows used).
- `Memberships` and `Subscriptions` terminology.

In Auxilia's actual business domain:
- The organization processes tax, pension, and welfare dossiers (**CAF and Patronato**).
- `Memberships` actually represent **Offered Services** (e.g., *Form 730 Tax Return*, *ISEE Certification*, *NASpI Unemployment Benefit*, *Civil Disability*, *ADI Inclusion Allowance*).
- `Subscriptions` actually represent **Cases / Dossiers** opened on behalf of a citizen.
- `MembershipFolderTemplates` represent the **Document Folders** associated with each case/service type.

The new schema formally adopts real-domain terminology (`dossier.services`, `dossier.cases`, `dossier.folder_templates`), purging irrelevant fitness concepts from the database.

---

## 4. New Domain Schema Architecture

Rather than clustering 29 tables inside the `public` schema, the new architecture organizes entities into **5 dedicated schemas**:

```
auxilia
├── iam          (Identity & Access Management: users, roles, assignments, sessions)
├── dossier      (Patronato/CAF services, cases/dossiers, folder templates)
├── dms          (Document Management System: files, categories, storage providers)
├── scheduling   (Appointments with GIST anti-overlapping constraints)
└── system       (Configurations, audit logs, i18n, UI configurations)
```

---

## 5. Entity Details and Architectural Decisions

### 5.1. Schema `iam`
- `iam.roles`: Unique role codes (`administrator`, `employee`, `client`, `system_configurator`).
- `iam.specializations`: Role specializations (e.g., CAF, Patronato) with dossier confidentiality flags.
- `iam.users`: Person master records.
  - `fiscal_code char(16)`: `CHECK (fiscal_code ~ '^[A-Z0-9]{16}$')` constraint and `UNIQUE` constraint.
  - `username citext` and `email citext`: Nullable (for citizens serviced offline), but strictly `UNIQUE` when provided.
  - `date_of_birth date`: Pure calendar date type.
- `iam.user_assignments`: Replaces the two self-referencing foreign keys (`AssignedEmployeeId`, `AssignedAdministratorId`). Allows tracking assignments between clients and operators/supervisors in an extensible manner with validity date ranges and assignment roles.

### 5.2. Schema `dossier`
- `dossier.service_categories`: High-level service categories.
- `dossier.services`: Master catalog of provided services with `numeric(10,2)` pricing and standard validity periods.
- `dossier.folder_templates`: Hierarchical folder tree for each service.
- `dossier.cases`: The actual case/dossier (formerly Subscription). Includes `case_number`, dates, assigned operator, workflow status (`Draft`, `Inserted`, `InProgress`, `Sent`, `Completed`, `Rejected`, `Cancelled`), amount paid, and tax reference year.

### 5.3. Schema `dms`
- `dms.storage_providers`: Registers storage endpoints (Azure Blob, Local, FTP), decoupling infrastructure details from file records.
- `dms.categories`: Normalized taxonomy replacing the 94 unconstrained strings previously found in the `Area` column.
- `dms.documents`: Document metadata.
  - Stores `storage_relative_path` (no more hardcoded absolute URLs!).
  - Added `content_sha256` column for file deduplication and integrity verification.
  - Optional FK to `dossier.cases(id)` and `dossier.folder_templates(id)`.

### 5.4. Schema `scheduling`
- `scheduling.appointments`: Manages appointments between clients and operators.
  - `time_window tstzrange` column combining start and end timestamps.
  - **GIST Exclusion Constraint:**
    ```sql
    CONSTRAINT no_overlapping_operator_appointments 
    EXCLUDE USING gist (
        operator_id WITH =,
        time_window WITH &&
    ) WHERE (status NOT IN ('Cancelled', 'NoShow'))
    ```
    Guarantees at the database kernel level that an operator cannot be booked for overlapping appointments.

### 5.5. Schema `system`
- `system.configurations`: Strongly typed EAV table (`string`, `integer`, `boolean`, `json`, `color`).
- `system.email_settings`: **Protected Singleton** (`CHECK (id = 1)`) with encrypted credentials.
- `system.ui_views` and `system.ui_role_permissions`: Unification of `ModuleConfigurations` and `PageConfigurations`. Models navigation items, page hierarchies, and role-based permissions/grid preferences.
- `system.languages`, `system.resource_keys`, `system.resource_translations`: Internationalization with a strict `UNIQUE(resource_key_id, language_id)` constraint.
- `system.app_logs`: Application logs using native `inet` for IP addresses and an optional FK to `iam.users`.

---

## 6. Security and Credential Protection

1. **SMTP Email:** The sender account password is no longer stored in plaintext in the database. The column is renamed to `password_encrypted` to support asymmetric keys or secret managers (e.g., Azure KeyVault / DPAPI / pgcrypto).
2. **IP Addresses:** Stored with the `inet` type, providing syntactic validation and preventing injection inside log fields.
3. **Access Control:** UI view permissions are linked with strict foreign keys to `iam.roles(id)`.

---

## 7. Data Normalization and Deduplication Strategy

The migration plan includes the following automated remediation steps:
1. **Citizen Deduplication:**
   - Group by `FiscalCode`.
   - Select the "Master Record" (the one with an active account, valid email, or created first).
   - Re-link all documents (`dms.documents`) and cases (`dossier.cases`) to the Master ID.
   - Delete orphaned duplicate records before applying the `UNIQUE` constraint.
2. **Sanitize Dummy Credentials:**
   - Convert `Username = '/'` and `Email = '/'` to `NULL`.
3. **Normalize File Paths:**
   - Extract only the filename/relative path from `https://.../documents/xyz.pdf` $\rightarrow$ `xyz.pdf`.
4. **Document Area Taxonomy Mapping:**
   - Reclassify the 94 ad-hoc string variations into standardized categories (`730`, `ISEE`, `ADI`, `INVALIDITA_CIVILE`, `NASPI`, `PENSIONI`, `LOCAZIONI`, `ALTRO`).

---

## 8. Mapping Matrix (Old Schema → New Schema)

| Old Table (`public`) | New Table | Transformation Notes |
|---|---|---|
| `Users` | `iam.users` | Fiscal code enforced as unique; dummy credentials converted to NULL; birth date changed to `date`; custom fields mapped to `metadata`. |
| `Users` (Self-ref FKs) | `iam.user_assignments` | `AssignedEmployeeId` and `AssignedAdministratorId` migrated into an explicit assignment table. |
| `Roles` | `iam.roles` | Standardized lowercase codes (`administrator`, `employee`, etc.). |
| `RoleSpecializations` | `iam.specializations` | `UNIQUE(role_id, name)` constraint added. |
| `UserRoles` | `iam.user_roles` | Join table retained with FKs to normalized roles. |
| `UserRoleSpecializations` | `iam.user_specializations` | Join table retained. |
| `UserSessions` | `iam.user_sessions` | IP column migrated to `inet` type. |
| `RegistrationRequests` | `iam.registration_requests` | Aligned column naming (`first_name`, `last_name`, `date_of_birth` as `date`). |
| `Memberships` | `dossier.services` | Refactored domain concept from "gym membership" to "CAF service". Price set with fixed scale `numeric(10,2)`. |
| `MembershipFolderTemplates` | `dossier.folder_templates` | Retained hierarchical folder tree linked to the service. |
| `Subscriptions` | `dossier.cases` | Terminology updated to cases/dossiers. Added `case_number` and formalized workflow status. |
| `UserDocuments` | `dms.documents` | Absolute URLs reduced to relative paths; `Area` mapped to `dms.categories`; added `sha256` content hash column. |
| `Appointments` | `scheduling.appointments` | Replaced date and duration with `time_window tstzrange` protected by a `GIST` anti-overlapping constraint. |
| `SystemConfigurations` | `system.configurations` | Added data type validation and `is_public` flag. |
| `EmailConfigurations` | `system.email_settings` | Converted to Singleton (`CHECK id = 1`), protected password, port check constraint. |
| `ModuleConfigurations` + `PageConfigurations` | `system.ui_views` + `system.ui_role_permissions` | Merged redundant tables into a view catalog and permission matrix with FKs to actual roles. |
| `Languages` | `system.languages` | Schema moved to `system`. |
| `ResourceKeys` | `system.resource_keys` | Cleaned up column naming. |
| `ResourceTranslations` | `system.resource_translations` | Added critical `UNIQUE(resource_key_id, language_id)` constraint. |
| `AppLogs` | `system.app_logs` | IP column migrated to `inet`; added optional FK to `iam.users(id)`. |
| `Requests` | `system.user_requests` | Manages client communications and service requests. |
| `Notifications` | `system.notifications` | Schema moved to `system`. |
| `Imports` / `ImportTypes` | `system.import_batches` / `system.import_templates` | Normalized imports and re-established missing FK. |
| `WorkoutPlans` | *(Deprecated)* | Unused gym legacy table (0 records), excluded from the new schema. |
