# F27 — Dashboards per role

Status: [~] done except tasks in the "today" list (B-26) · Tasks: B-23, B-24, B-26 · Quirks: Q41, Q42, Q43

## Legacy behaviour
**Administrator** `/admin`: "Welcome, {name}"; cards: total employees, total clients, active subscriptions (link), pending requests (link), "my clients" (shows the same total clients value), pending registrations (link). Charts (Chart.js): subscriptions per membership (filter all/month/week by start date), revenue per month = sum `AmountPaid` by start month (filter all/month/week, Q43). Appointments grid (all from yesterday on; employee, client, date, duration, status).
**Employee** `/employee`: cards my clients, total CAF clients, total PATRONATO clients (custom fields, Q41), upcoming appointments (mine), pending requests (module `Requests`), pending registrations (module `RegistrationRequests`, Q42). Charts: appointments per day (week/month/all), status distribution Pending/Approved/Completed/Cancelled (current/past month). Grid of global-calendar appointments not completed.
**Client** `/client`: active subscription (name + expiry) or "no active subscription", upcoming appointments count (link), active workout plans (always 0, **dropped** — F18), last 5 appointments list.

## Acceptance criteria
- [x] Single `/dashboard` with widgets per role reproducing every card/chart above with working filters (week/month/year/all). *(B-23, B-24)*
- [x] KPI cards link to the filtered list.
- [x] Employee "custom field counters": one card per boolean custom field flagged as dashboard counter (CAF/PATRONATO seeded for migrated tenant). *(B-23; seed with E-05)*
- [ ] New: "today" list (appointments today, cases due, tasks), cases expiring soon.
- [x] Counts respect F10 visibility. *(B-23: same scope as the case lists)*
