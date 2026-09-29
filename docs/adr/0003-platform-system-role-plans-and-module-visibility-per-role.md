# ADR 0003 — Platform System role, plans and module visibility per role

- Status: Accepted (2026-09-29)
- Related decisions: D-18, D-21, D-22, D-25 (`docs/decisions.md`)

## Context
Legacy `SystemConfigurator` lived inside the tenant. With many tenants and future pricing, technical administration and module entitlement must be centralized.

## Decision
A platform role `System` (Catalog users, mandatory TOTP 2FA) is the only one that manages tenants (create, suspend, archive — never physical delete), plans, module visibility per tenant **and per role**, and each tenant's technical configuration (settings, branding, sending accounts, grids, labels, custom fields, permissions, specializations, imports, logs, manual jobs). It works through short-lived platform tokens on `/api/v1/platform/…` and cannot read or change business data. Module visibility = Core, or (tenant override ?? plan) includes the module for the role, and the role has permissions; hidden modules return 404 and disappear from navigation. Tenant roles: Administrator, Employee, Client.

## Consequences
Pricing becomes data (plans), not code. Tenant administrators can no longer change technical configuration themselves.

## Alternatives considered
Keep a tenant-level configurator role (rejected by the user).
