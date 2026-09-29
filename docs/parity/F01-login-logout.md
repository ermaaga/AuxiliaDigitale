# F01 — Login / logout

Status: [ ] not started · Tasks: P2-01, P2-02, P2-07, P3-03, P3-06, P3-09 · Quirks: Q01, Q44, Q56, Q57, Q58

> **Decisions:** D-05 (login access independent from client status), D-06 (activation link), D-08 (single session = tenant setting, default **off**; the "force logout of other sessions" criterion applies only when enabled). No `SystemConfigurator` redirect: System users log in to the platform console (D-18).

## Legacy behaviour
- `Components/Login.razor` (`/login`, `EmptyLayout`): username + password + "Remember username" checkbox. Username lookup is case-insensitive; password verified with BCrypt (`UserService.AuthenticateAsync`).
- On success: `ForceLogout` pushed to every other connection of the same user (single session), session stored in cache (`SessionService`, cookie `AuxiliaSessionId`, 120 min), claims `NameIdentifier`, `Name`, `FullName`, `LanguageId`, roles.
- Redirect by highest role: Administrator → `/admin`, SystemConfigurator → `/system/configurations`, Employee → `/employee`, else `/client`.
- Failure message: "Invalid username or password OR The user is Inactive" (but inactive users are **not** blocked, Q01). Unexpected errors written to `AppLog` (source `Login`).
- Login page shows app name (`AppName` config) or `/icon.png` depending on setting `UseAppName`; background from branding (`BackgroundService`).
- Already-authenticated users opening `/login` are redirected to their dashboard (`AuthChecker`).
- Logout (header menu): clears session, navigates to `/login`.

## Business rules
1. Username match is case-insensitive.
2. Existing BCrypt hashes must keep working (verify, then rehash).
3. After login the user lands on the dashboard of its highest-priority role.
4. A new login invalidates the user's other sessions when single-session is enabled (D-08).

## Acceptance criteria
- [ ] Given a legacy user with a BCrypt hash, when it logs in with the right password, then login succeeds and the hash is upgraded to the Identity format.
- [ ] Given username `MARIO.ROSSI`, when the stored username is `mario.rossi`, then login succeeds.
- [ ] Given wrong credentials, then a generic error is shown (no user enumeration) and a failed-attempt counter increments; lockout after the configured threshold.
- [ ] Given a user with roles Administrator + Employee, when it logs in, then it lands on the dashboard with administrator widgets.
- [ ] Given "remember username" checked, when the user returns to the login page, then the username is prefilled; unchecked → cleared.
- [ ] Given single-session ON and a user logged in on browser A, when it logs in on browser B, then A receives `ForceLogout` and is sent to login.
- [ ] Given an authenticated user, when it opens `/{tenant}/login`, then it is redirected to the dashboard.
- [ ] Given logout, then the refresh session is revoked, the BFF cookie is cleared and the access token `jti` is deny-listed.
- [ ] Login page shows logo or app name according to `UseAppName` and the tenant background.

## Improvements
JWT ES256 + rotating refresh token, BFF httpOnly cookie, lockout, forgot/reset password by e-mail, rate limit per IP.
