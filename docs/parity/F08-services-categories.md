# F08 — Services (Membership) and categories (MembershipType)

Status: [ ] not started · Tasks: 4.15, 4.23 · Quirks: Q26, Q27, Q28

## Legacy behaviour
- `Admin/Memberships.razor` (`/admin/memberships`, module `Memberships`): collapsible create/edit form: name (required), price €, duration days, type (MembershipType dropdown), specialization (Employee-role), active (edit only), description. Grid (config `Memberships/Administrator`): name, description, specialization, price, duration, status. Sort by name/price (default name). Actions: detail, delete (confirm, hard delete).
- `Admin/MembershipDetail.razor` (`/admin/memberships/{id}`): header price/duration/active; edit name, description, price, duration, specialization, active; list of subscriptions of this membership (client with photo, start, end, amount, active/expired; row click → subscription detail; export CSV/PDF); **folder template editor** (F33).
- MembershipType: service CRUD (`Create/Update/DeleteMembershipTypeAsync`) but no UI (Q26).
- Seed (dev): ISEE (€10, 1 day), 730 (€50, 7 days), F24 (€150, 30 days) with Italian descriptions.

## Acceptance criteria
- [ ] `/services` list with name, category, specialization, price, duration, active; filters; export.
- [ ] Create/edit service incl. category and specialization (specialization saved on create — Q27).
- [ ] Deactivate instead of delete when cases exist; delete allowed only when unused (Q28).
- [ ] Service detail shows its cases (paged, export, click → case).
- [ ] Categories CRUD page (Q26).
- [ ] Price stored as `numeric(12,2)` + currency EUR.
- [ ] Folder template editor on service detail (F33).
