# F31 — Password tool

Status: [ ] not started · Tasks: P2-07 

## Legacy behaviour
- `Auxilia.Tools.Password` console: option 1 = hash a password with BCrypt; option 2 = verify a password against a hash.

## Acceptance criteria
- [ ] `auxctl users reset-password --tenant <slug> --user <username|email> [--send-link]` sets a new password (Identity hasher) or sends a reset link; audit event logged.
- [ ] `auxctl users verify-legacy-hash` (support tool) verifies a password against a legacy BCrypt hash.
