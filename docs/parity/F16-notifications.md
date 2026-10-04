# F16 — Notifications

Status: [~] done except the expiry producer (B-25) · Tasks: P2-05, B-19, B-21 · Quirks: Q12, Q13

## Legacy behaviour
Entity `Notification`: UserId, Title, Message, Type (`Info`, `Request`, `RegistrationRequest`, `Appointment`, `Subscription`, `WorkoutPlan` (dropped, F18), …), IsRead, IsCreatedByFinalUser, CreatedAt, RelatedEntityId.
- Header bell with unread badge; offcanvas panel shows last 10 (icon per type, "New" badge, date), click → mark read + navigate by type (Q12), delete single. Polling every **30 s**.
- Producers: registration (admins), client request (employee/admins), admin reply (sender), appointments (create/update/approve/reject by staff → client; client request → employee), expiry job (client).
- `NotificationService` also drives UI modals (`NotificationModal`: success/error/warning/info) and toasts (`ToastContainer`).
- SignalR hub `/sessionhub`: `ForceLogout`, `ReceiveNotification(type, message)` (documents).

## Status notes
- P2-05: hub `/hubs/notifications` (SignalR, JSON): a connection joins `t:{slug}`, `t:{slug}:u:{userId}`, `t:{slug}:role:{role}` (every role) and `t:{slug}:s:{sessionId}`; server-to-client only. `IRealtimeNotifier` (Application port) pushes to user, role, session or tenant of the current tenant, best effort (`AUX-18003` on failure). Redis backplane (`auxilia:signalr`) so every Api node and the Worker reach every connection. Event names in `Contracts.Realtime.RealtimeEvents` (`NotificationReceived`, `ForceLogout`). Still to do: notification entity, producers, center, preferences (B-19, B-21), client connection in the web app (P3).

## Acceptance criteria
- [x] Notification center: badge with unread count, list paged, mark one/all as read, delete, deep link per type **and role**. *(API B-19; bell and page B-21)*
- [x] Real-time delivery via SignalR `NotificationReceived` (no polling needed; fallback polling if disconnected). *(server push B-19; client B-21, polling 30 s only without hub)*
- [ ] All legacy producers above emit notifications with typed entity links. *(B-19: registration, requests, appointments; expiry job B-25)*
- [x] Notification preferences per type (in-app/e-mail). *(API + e-mail B-19; page B-21)*
- [ ] Toasts for operation feedback; blocking dialogs only for confirmations/errors.
