# ADR 0002 — Multi-tenancy with Catalog DB and database per tenant

- Status: Accepted (2026-09-29)
- Related decisions: D-02, marketplace #2, #6 (`docs/decisions.md`)

## Context
Each customer's data must be isolated, individually backed up and migrated; the platform must host N tenants.

## Decision
A Catalog DB (tenants, domains, modules/plans, platform users and settings, client apps, migration runs, Data Protection keys) plus one PostgreSQL database per tenant with one schema per module. Tenant resolved from JWT claim → `X-Tenant` → host; web URLs `/{tenantSlug}/…`. `auxctl` creates tenant databases (`CREATEDB`), `--existing-database` for DBA-created ones. Everything carries the tenant: cache keys, files, logs, messages, SignalR groups, locks.

## Consequences
Strong isolation and per-tenant restore; more databases to migrate — handled by `auxctl migrate tenants` with per-tenant status.

## Alternatives considered
Shared schema with tenant column (rejected: weaker isolation, harder per-tenant restore).
