# F10 — Private cases and employee visibility rules

Status: [~] in progress (cases B-08/B-09 and documents B-12 done; pages B-14, exports/dashboards B-22/B-23) · Tasks: B-09, B-12, B-14 · Quirks: Q09, Q10, Q11 · **Decision D-04: code semantics confirmed**

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

## Backend (B-08, B-09)
`CaseAccessPolicy` holds the rules: `CanSeeAsync`/`CanManageAsync`/`CanDeleteAsync` for one case (detail and writes,
through `IAccessGuard`) and `ScopeAsync` for the lists, translated into SQL by `ICaseData.PageAsync` (no
post-filtering). "Show all" off = `OnlyHeldOrUnspecialized`; with "show all" on the D-04 visibility still applies (the
legacy list also showed private cases of specializations not held: D-04 confirms the code rule of visibility).

## Acceptance criteria (D-04: code semantics)
- [ ] Rules implemented as a query-level policy (`VisibleTo(user)`), never as UI filtering; applied to lists, detail, documents, downloads, ZIP, search, exports, dashboards counts.
- [x] Non-visible case/document by id → `404` (Q10).
- [ ] Test matrix (Application + Api integration): {no spec, non-private spec, private spec held, private spec not held} × {case, case document, client document with/without assignment} × {Admin, Employee}.
- [ ] Employee can manage only per `CanManageSubscription`/`CanManageDocument`; forbidden actions return `403` with code.
- [x] Employee can create cases only for allowed services.
