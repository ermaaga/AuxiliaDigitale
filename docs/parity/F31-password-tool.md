# F31 — Password tool

Status: [x] done (P2-07) · Tasks: P2-07 

## Legacy behaviour
- `Auxilia.Tools.Password` console: option 1 = hash a password with BCrypt; option 2 = verify a password against a hash.

## Acceptance criteria
- [x] `auxctl users reset-password --tenant <slug> --user <username|email> [--send-link]` sets a new password (Identity hasher) or sends a reset link; audit event logged.
- [x] `auxctl users verify-legacy-hash` (support tool) verifies a password against a legacy BCrypt hash.

## Status notes
- P2-07: `auxctl users reset-password --tenant <slug> --user <user name | e-mail> [--send-link]`. Without `--send-link` it sets a random temporary password (at least 16 characters, every character class, checked against the tenant policy and history, F35), printed once, ends every session of the user and marks the account `must_change_password`: the next sign-in answers 403 `AUX-12043` and the user sets their own password with `POST /auth/password/change` (the expired-password flow), even when expiry is disabled. With `--send-link` it e-mails the usual reset link (needs an e-mail account for the tenant, N03, else `AUX-25011`). The user is looked up by user name, then by e-mail (`AUX-12051` when several users share it). Audit: security event `AUX-29023` (Warning) and operation `AUX-12050`.
- `auxctl users verify-legacy-hash` reads the BCrypt hash and the password from standard input (one per line, never on the command line); prints `match` (exit 0) or `no match` (exit 2). Hashing a password is no longer needed: passwords are set through the flows above (the legacy option 1 is intentionally not reproduced).
