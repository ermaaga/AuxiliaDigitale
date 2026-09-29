# F10 — Private cases and employee visibility rules

Status: [ ] not started · Tasks: 4.17, 4.20, 4.22 · Quirks: Q09, Q10, Q11 · **Decision D-04 required**

## Legacy behaviour (code = source of truth)
Let `S(e)` = specializations assigned to employee `e` (`UserRoleSpecializations`).

**Case visibility for an employee** (`SubscriptionService.GetUserSubscriptionsAsync(userId, employeeId)`):
case visible ⇔ `case.SpecializationId == null` ∨ `!case.Specialization.PrivateSubscriptions` ∨ `case.SpecializationId ∈ S(e)`.

**Employee case list** (`GetAllSubscriptionsPagedAsync(req, employeeId)`): when "Show all practices" is **off** the list is restricted to `SpecializationId == null ∨ SpecializationId ∈ S(e)` (regardless of the private flag). When on, no specialization filter at all (note: private cases of other specializations are then listed — to confirm with D-04).

**Document visibility** (`DocumentManager.ApplyEmployeeAccessFilter`):
- document linked to a case → same rule as case visibility;
- document without case → visible ⇔ client assigned to `e` ∨ client has **no** private specialization outside `S(e)`.

**Management rules (UI)**
- `CanManageSubscription` (employee client detail): case with no specialization, or specialization ∈ `S(e)` → can open detail / delete; delete hidden when Completed.
- `CanManageDocument` (employee documents): document of a case with specialization → only if ∈ `S(e)`; otherwise yes. Edit button only when the client is assigned to `e`.
- New case by employee: only services with no specialization or specialization ∈ `S(e)`.
- Administrators see and manage everything.

**Documentation divergence**: `documentations/PRIVATE_SUBSCRIPTIONS.md` and `data-model.md` state "only the assigned employee can see private cases". The code does **not** check the assigned employee for cases.

## Acceptance criteria (after D-04; default = code semantics)
- [ ] Rules implemented as a query-level policy (`VisibleTo(user)`), never as UI filtering; applied to lists, detail, documents, downloads, ZIP, search, exports, dashboards counts.
- [ ] Non-visible case/document by id → `404` (Q10).
- [ ] Test matrix (Application + Api integration): {no spec, non-private spec, private spec held, private spec not held} × {case, case document, client document with/without assignment} × {Admin, Employee}.
- [ ] Employee can manage only per `CanManageSubscription`/`CanManageDocument`; forbidden actions return `403` with code.
- [ ] Employee can create cases only for allowed services.
