# F09 — Cases (Subscription) workflow

Status: [ ] not started · Tasks: B-08, B-09, B-14, B-26 · Quirks: Q02, Q03, Q04, Q10, Q24

## Legacy behaviour
**Entity**: `Subscription` (UserId, MembershipId, StartDate, EndDate?, IsActive, AmountPaid, Status, IsRejected, RoleSpecializationId?, CustomFields, RowVersion).
**Status machine** `SubscriptionStatus`: Inserted(0) → InProgress(1) → Sent(2) → Completed(3). Labels IT: Inserito, In Elaborazione, Inviata, Conclusa. Colors: #3498db, #f1c40f, #2ecc71, #9b59b6 (progress 0/33/66/100%).

**Create** (`SubscriptionService.CreateSubscriptionAsync`) from: admin `/admin/subscriptions` panel (client searchable, employee select "default employee", membership searchable showing "name - €price (days)", start date), admin client detail, employee client detail.
- Client without assigned employee → default employee assigned. Admin panel also (re)assigns the client to the chosen employee.
- `EndDate = null`, `AmountPaid = membership.Price` (Q02), `Status = Inserted`, `IsActive = true`, `RoleSpecializationId = given ?? membership.RoleSpecializationId`.
- Client status recomputed (becomes active, Q03).

**Detail** (`Admin|Employee/SubscriptionDetail.razor`, `/…/subscriptions/{id}`): header "membership — client"; client, start, end; progress bar with Back/Forward buttons.
- Forward: confirmation "ConfirmStatusChange"; moving to Completed opens a modal: amount paid (prefilled with membership price) + "rejected" checkbox → `UpdateSubscriptionStatusAsync(Completed, amount, rejected)` which sets `EndDate = now`, `AmountPaid`, `IsRejected`, recomputes client status.
- Back: allowed from InProgress/Sent (not from Inserted or Completed), no confirmation.
- Any change on a Completed subscription throws ("Cannot change status of a completed subscription").
- Per-status content: **Inserted** → membership info (name, description, price, duration); **InProgress** → documents area (folder tree F33, uploader, document grid with download/move/delete, ZIP); **Sent** → summary (membership, client, start, "N file caricati"); **Completed** → summary incl. end date and amount paid (and rejected).
- Employee version: documents filtered by private rules; back button returns to `/employee/subscriptions` when opened with `?from=subscriptions`, else to the client.

**Lists**
- Admin `/admin/subscriptions`: grid user, membership, start, end, amount, status badge (Completed+rejected shows "Rejected" red), specialization; filters client name (name or surname), membership name; sort client (surname,name), membership, start, end, amount (default start desc); export CSV/PDF enabled; actions detail, delete (confirm).
- Employee `/employee/subscriptions`: toggles "Show all practices" (off ⇒ only cases with no specialization or employee's specializations) and "Show completed" (off ⇒ exclude Completed); columns client (surname name), membership, start, end, specialization, status; action detail.
- Client `/client/subscriptions` (module `Subscription`): own cases: name, start, end, status Active / Expired (end < now) / Inactive.
- Client detail grids (see F05). Admin dashboard counts active subscriptions.

**Delete**: hard delete + client status recomputed.

## Acceptance criteria
- [ ] Domain: `Advance()` Inserted→InProgress→Sent→Completed; `GoBack()` only from InProgress/Sent; Completed is terminal (any change → `409` coded error).
- [ ] Completing requires amount paid (default = service price) and rejected flag; sets `closed_at`/`expires_on = today`, records a payment and a history row; recomputes client status.
- [ ] Every transition writes `case_status_history` (who, when, from, to, note) shown as a timeline.
- [ ] Create case: default employee assignment, price snapshot, specialization default from service, case number `{year}-{seq}`; client becomes active.
- [ ] Case detail shows contextual content per status exactly as listed above.
- [ ] Case list with filters/sorts listed above; employee toggles "show all" / "show completed" (defaults off/off).
- [ ] Client sees own cases with Active/Expired/Inactive badge.
- [ ] Soft delete with confirmation; employees cannot delete Completed cases (F10).
- [ ] Manual action "Send expiry reminder" e-mail (F11, Q24).

## Improvements
Status history/timeline, multiple payments, document checklist (B-26), wizard creation, actions per status.
