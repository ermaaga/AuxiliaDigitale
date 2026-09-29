# ADR 0008 — Pluggable adapters for external capabilities

- Status: Accepted (2026-09-29)
- Related decisions: D-16, D-19, D-20, D-30 (`docs/decisions.md`)

## Context
Login methods, message channels and storage will grow (Google for mobile clients, WhatsApp through an external endpoint, OTP by e-mail).

## Decision
Each capability is a port in `Application/Abstractions/<Capability>` with adapters in `Infrastructure/Adapters/<Capability>/<Provider>` registered as keyed services and selected by settings (platform default, tenant override). Initial adapters: login `password`, `email-otp`; channel `email/smtp`; storage `local`, `ftp`, `azure-blob`; captcha `none`, `altcha`; PDF `migradoc`. Google and WhatsApp HTTP gateway are modelled but not implemented.

## Consequences
New providers without touching modules; each adapter needs its own settings definition and tests.

## Alternatives considered
Hard-coded integrations (legacy approach).
