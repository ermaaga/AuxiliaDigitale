# N04 — Authenticator app (MFA) and "stay signed in" for the users of a tenant

Status: [x] done (S-09, 2026-10-08) · Decided with the user on 2026-10-08 · Related: F01, F35, N02 (TOTP of System users), D-22

## Goal
Every user of a tenant (Administrator, Employee, Client) can protect the account with an authenticator app (TOTP,
RFC 6238): at sign-in, after the password, the app's 6-digit code is asked. It is **optional**; the tenant can make it
mandatory for some roles. A "stay signed in" box keeps the session across browser restarts. Passwords are never stored
by the application in the browser: saving them is left to the browser's own password manager (the sign-in form already
declares `autocomplete="username"` / `current-password`).

## Rules
- **Factors**: password + code (true MFA). The code never replaces the password. With the e-mailed sign-in code
  (`email-otp`, F35) the app's code is asked too.
- **Optional**: setting `auth.mfa.requiredRoles` (roles that must use it; empty by default). A user of such a role
  without the app cannot finish the sign-in: after a correct password the API answers "setup required" and the user
  enrols the app on a public page with user name and password as the credential (like an expired password, F35).
- **Enrolment**: QR code of the `otpauth://` URI (issuer = the tenant's app name, account = the user name) and the setup
  key for manual entry; confirmed by a current code. The secret is protected with Data Protection (purpose per tenant).
  Proposed (with "Later") after the first temporary password is changed; always available in the profile.
- **Codes**: current step ± 1; a step already used is refused (replay); a wrong code counts as a failed sign-in (same
  progressive lockout as the password, F35); every attempt is in the sign-in audit.
- **Disable**: from the profile with the current password; refused when the user's roles require the app.
- **Reset** (lost phone): an Administrator from the user's page (employee / client), the platform staff with
  `auxctl users reset-mfa`. Reset ends the user's sessions; the user enrols again (at the next sign-in when required).
  Security events for enable, disable, reset and failed codes.
- **Stay signed in**: a box on the sign-in page. Without it the BFF cookie is a session cookie (closed browser =
  signed out) and the API session follows the idle timeout (`auth.session.idleMinutes`). With it the API session stays
  open up to `auth.session.rememberMeDays` (default 14, 0 = the box is hidden), never beyond
  `auth.session.absoluteDays`, and the cookie lasts as long. The session stays revocable (logout, password change,
  MFA reset, sessions page).

## Acceptance criteria
- [x] A user enables the app from the profile (QR + code), signs out, signs in with password + code; a wrong code is refused and counted; the same code cannot be used twice.
- [x] After changing the temporary password the user is offered the app setup and can postpone it.
- [x] With `auth.mfa.requiredRoles` containing the user's role, a user without the app is led to the setup page after the password and enters only after confirming a code.
- [x] Disable from the profile needs the password and is refused when the role requires the app.
- [x] An Administrator resets the app of a user (sessions end, security event); `auxctl users reset-mfa` does the same.
- [x] "Stay signed in" keeps the session after the browser is closed (persistent cookie up to the configured days); without it the cookie ends with the browser.
- [x] EN + IT texts; API, Application and E2E tests.
