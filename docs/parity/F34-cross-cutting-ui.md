# F34 — Cross-cutting UI behaviours

Status: [~] done except the UAT per role (user acceptance); verified in H-04 · Tasks: P2-04, P3-02, P3-06, P3-07, B-03, B-21, B-23 · **Not listed in the blueprint — added by the legacy analysis.** · Quirks: Q35, Q39, Q41, Q44, Q56, Q57

## Legacy behaviour
- Single active session per user (new login force-logs-out others) — F01/F17.
- "Remember username" on login (localStorage).
- Light/dark toggle in header (session-persisted) — F04.
- `UseAppName`: header and login show app name text instead of logo.
- Login background from branding; theme colors on card headers/buttons.
- Header: hamburger on mobile, theme toggle, notification bell with badge (30 s polling), profile image/initials, dropdown My profile / Logout.
- Confirmation modal for destructive actions (`ConfirmationService.ConfirmDeleteAsync` / `ConfirmAsync` with info/warning/danger styles); toasts success/error/warning/info; modal messages.
- `SearchableSelect` (type-ahead select) for clients, memberships, users, parent pages, entities.
- Collapsible "create" panels at the top of list pages; row click to detail on some grids; back buttons honouring origin (`?from=subscriptions`).
- Status badge colors: case statuses (see F09), appointment Pending=warning, Approved=success, Rejected=danger, Completed=info.
- Dates shown as `MMM dd, yyyy` / `MMM dd, yyyy HH:mm`; money `€0.00`; file sizes B/KB/MB/GB.
- Custom-field KPI cards on employee dashboard (CAF/PATRONATO) — F27.

## Acceptance criteria
- [ ] Every behaviour above exists in the new UI (or its explicitly approved replacement) — checked in UAT per role.
- [x] Dates/numbers formatted per user language and tenant time zone. *(H-04: next-intl formatter with the user language; appointment dates in the tenant time zone (`TenantTime`))*
- [x] Destructive actions always confirm; non-destructive actions give toast feedback. *(H-04: `useConfirm()` before every delete/reset/revoke, `useNotify()` toasts)*
- [x] Type-ahead combobox for every large lookup. *(H-04: `Combobox` for clients, employees, services, tenants)*
- [x] Theme preference persisted; branding applied on public pages too. *(H-04: theme saved in the profile and applied after sign-in; `PublicPage` applies the tenant branding)*
