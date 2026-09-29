# F16 — Notifications

Status: [ ] not started · Tasks: P2-05, 4.27, 4.29 · Quirks: Q12, Q13

## Legacy behaviour
Entity `Notification`: UserId, Title, Message, Type (`Info`, `Request`, `RegistrationRequest`, `Appointment`, `Subscription`, `WorkoutPlan` (dropped, F18), …), IsRead, IsCreatedByFinalUser, CreatedAt, RelatedEntityId.
- Header bell with unread badge; offcanvas panel shows last 10 (icon per type, "New" badge, date), click → mark read + navigate by type (Q12), delete single. Polling every **30 s**.
- Producers: registration (admins), client request (employee/admins), admin reply (sender), appointments (create/update/approve/reject by staff → client; client request → employee), expiry job (client).
- `NotificationService` also drives UI modals (`NotificationModal`: success/error/warning/info) and toasts (`ToastContainer`).
- SignalR hub `/sessionhub`: `ForceLogout`, `ReceiveNotification(type, message)` (documents).

## Acceptance criteria
- [ ] Notification center: badge with unread count, list paged, mark one/all as read, delete, deep link per type **and role**.
- [ ] Real-time delivery via SignalR `NotificationReceived` (no polling needed; fallback polling if disconnected).
- [ ] All legacy producers above emit notifications with typed entity links.
- [ ] Notification preferences per type (in-app/e-mail).
- [ ] Toasts for operation feedback; blocking dialogs only for confirmations/errors.
