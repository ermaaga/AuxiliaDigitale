# ADR 0009 — Outbound messaging with N accounts chosen by purpose and sender role

- Status: Accepted (2026-09-29)
- Related decisions: D-16, D-31 (`docs/decisions.md`)

## Context
The legacy had one SMTP configuration with a clear-text password; the user needs several SMTP servers selectable by role, and WhatsApp later.

## Decision
Module `Messaging`: `messaging_accounts` (channel, provider, settings, encrypted secret, default per channel) and `sender_rules` (channel, purpose Transactional/Notification/Marketing, sender role or any → account). Resolution: exact → any role → channel default. Every send goes through `IMessageDispatcher` → `outbound_messages` log → queue → channel adapter; templates are Liquid per language. Per-specialization SMTP (legacy branch) is excluded.

## Consequences
One place to answer 'was the e-mail sent, from which account?'.

## Alternatives considered
Single global SMTP (insufficient); SMTP per specialization (rejected, D-31).
