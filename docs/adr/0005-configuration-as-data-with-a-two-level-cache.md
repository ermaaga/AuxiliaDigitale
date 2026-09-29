# ADR 0005 — Configuration as data with a two-level cache

- Status: Accepted (2026-09-29)
- Related decisions: D-16, D-18, D-28 (`docs/decisions.md`)

## Context
The legacy mixed business settings in appsettings and the DB; multi-tenancy requires per-tenant configuration without restarts.

## Decision
`appsettings` holds only infrastructure. Every other setting is a typed `SettingDefinition<T>` resolved user → tenant → platform → code default. Values live in the Catalog (platform) and tenant DB; a per-tenant immutable snapshot is served from HybridCache (L1 memory ~60 s, L2 Redis 30 min). Writes invalidate by tag after commit and publish on Redis so every node evicts its L1. Secrets are encrypted with Data Protection, also in cache. Same mechanism for translations, modules, permissions, navigation, branding and lookups.

## Consequences
Changes apply within seconds without restart; Redis outage degrades to L1 + DB.

## Alternatives considered
Free-form key/value settings (rejected: no validation, typos).
