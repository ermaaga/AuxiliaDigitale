# F17 — Active sessions and forced logout

Status: [ ] not started · Tasks: P2-02, P2-04, B-20, B-21 · Quirks: Q36, Q56, Q58

> **Decision D-08:** single session per user is a tenant setting, default **off**.

## Status notes
- P2-02: sessions in `identity.refresh_sessions` (user, client app, IP, user agent, created, last used, idle expiry `auth.session.idleMinutes` = 120 sliding, absolute expiry `auth.session.absoluteDays` = 14, end reason). With `auth.singleSession` a sign-in ends the other sessions of the user (their access tokens are denied at once); a password change or reset, a role change or a deactivation (new security stamp) ends the session at the next refresh, a reset ends them at once. `ISessionManager.EndSessionAsync` is the admin revocation primitive. Still to do: `ForceLogout` push (P2-05), `/sessions` page and API (B-20/B-21).

## Legacy behaviour
- `Admin/ActiveSessions.razor` (`/admin/sessions`, module `Sessions`): table user (full name + username), roles, IP (always "N/A"), login time, last activity (always now), duration, status; KPI cards total sessions, active now, unique users; manual refresh + auto refresh every 5 s. Data read from cache keys `session_*` / `data_{username}`.
- Forced logout: on login the hub sends `ForceLogout` to group `user_{username}` → other tabs log out and reload (single session, Q56). No admin "kill session" button in legacy UI.
- Session timeout 120 min (Q58).

## Acceptance criteria
- [ ] `/sessions` lists active refresh sessions with user, roles, client app, IP, user agent, created, last used, duration; KPIs; auto refresh.
- [ ] Admin can revoke a session → immediate `ForceLogout` push + deny-list of access token.
- [ ] Single-session per user honoured per D-08.
- [ ] Idle/absolute timeouts configurable per tenant (default equivalent to 120 min idle).
