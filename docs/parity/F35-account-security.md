# F35 — Account security: password policy, reset, expiry, history, OTP login, login audit

Status: [ ] not started · Tasks: P2-02, P2-08, B-27 · Decision D-30 · **Source: legacy branch `Security_Update` (`e314e9a`, 2026-06-23), not merged into `develop` but part of the parity baseline.**

## Legacy behaviour (branch `Security_Update`)
- **Settings** (seed `S_20260623_002`): `PasswordMinLength` 8, `PasswordRequireUppercase`/`Lowercase`/`Digit`/`SpecialChar` true, `PasswordHistoryCount` 3, `PasswordExpiryEnabled` false, `PasswordExpiryMonths` 6, `EnableOtpLogin` false. Edited in a new `PasswordSecurityConfigSection` of `/system/configurations`.
- **Password strength** (`ValidatePasswordStrengthAsync`): min length + required character classes from settings; errors are translation keys (`PasswordTooShort:{n}`, `PasswordRequiresUppercase`, …).
- **Password history** (`PasswordHistory` table): a new password may not match any of the last `PasswordHistoryCount` hashes; every change stores the hash; `User.PasswordChangedAt` updated.
- **Change password** page `/change-password` (authenticated), also used when the password is expired; profile links to it.
- **Password expiry** (`IsPasswordExpiredAsync`): when enabled, `PasswordChangedAt + PasswordExpiryMonths < now` → user must change password before continuing (`ChangePasswordRequired`).
- **Forgot / reset password**: `/forgot-password` asks the e-mail; always answers "If an account exists, a reset link has been sent" (no enumeration); token (`PasswordResetToken`, random, 24 h, single use) e-mailed as `{AppUrl}/reset-password?token=…`; `/reset-password` validates token, strength and history, then changes the password and marks the token used.
- **OTP login** (when `EnableOtpLogin`): on the login page "Login with OTP" → username → 6-digit code sent by e-mail, valid 10 minutes (stored in cache), single use → login.
- **Login audit** (`LoginAuditLog`: user, username, time, IP, type `Password`/`Otp`, success, failure reason `InvalidCredentials`/`InvalidOtp`): written on every attempt; page `/admin/login-audit` (Administrator, module/page `LoginAuditLogs`) with grid username (filter, sort), date (sort), type (filter), result, IP, failure reason.

## Acceptance criteria
- [ ] Password policy settings (tenant, edited by System — D-18) with the same defaults; server-side validation returns localized messages per rule; applied on activation, change, reset, admin-set password and import.
- [ ] Password history of N previous hashes enforced (Identity hashes; migrated BCrypt history kept as `LegacyBcrypt`).
- [ ] Change password (current password required) from the profile; forced change when expired (login returns a "password change required" state; the UI routes to the change form before anything else).
- [ ] Password expiry per settings; `password_changed_at` migrated.
- [ ] Forgot/reset password: no user enumeration, single-use token with configurable TTL (default 24 h), link to `/{tenant}/reset-password`, policy + history enforced, all sessions revoked after reset.
- [ ] OTP by e-mail as a pluggable login method (`email-otp`, `IAuthenticationMethod`), enabled by tenant setting (default off): 6-digit code, 10-minute TTL, single use, attempt limit + rate limit; appears in `GET /auth/methods`.
- [ ] Every login attempt (password, OTP, future external methods) stored in `identity.login_attempts` (user, username, time, IP, user agent, method, success, failure reason) and logged `29xxx`.
- [ ] Administrator page "Login audit" in the tenant app: filters username, method, result, date range; sort by username/date; export (F26).
- [ ] Legacy data migrated: `LoginAuditLogs` → `login_attempts`, `PasswordHistories` → password history, `PasswordChangedAt`; unused reset tokens not migrated.
