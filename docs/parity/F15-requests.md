# F15 — Requests

Status: [ ] not started · Tasks: 4.26, 4.29 · Quirks: Q16, Q17, Q18

## Legacy behaviour
Entity `Request`: SenderId, ReceiverId?, Type, Subject, Message, Status (`Pending`/`Responded`/`Closed`), Response, CreatedAt, RespondedAt.
- **Client** `/client/requests` (module `Requests`): list of own sent requests (status Pending/Responded) + view (type, message, response, "responded on"). "New request": checkbox "Ask my personal trainer" (default on; relabelled "Ask my operator"), type (Information / General / Support), subject*, message*. If ask-trainer and the client has an assigned employee → `ReceiverId = employee`, notification to that employee; otherwise `ReceiverId = null` and notification to **every Administrator** ("New Request — {name} submitted a new {type} request.", type `Request`).
- **Employee** `/employee/requests` (module `Requests`, disabled by default in seed): tabs "My clients' requests" (receiver = me) and "My requests" (sender = me); respond to Pending received requests (response required, status Responded, **no notification**, Q17); view; "New request" to admins (receiver null, no notification).
- **Admin** `/admin/requests` (module `Requests.UserRequests`): requests with receiver null only (Q16); columns name, type, subject, created, status; view/respond (response + notification to sender "Request Response"); delete (confirm).
- Sort: created at, status (default created desc). Dashboard counts pending requests (admin: all pending; employee: received pending).

## Acceptance criteria
- [ ] Client creates a request to its assigned employee (default) or to the office (admins); correct recipients notified.
- [ ] Employee creates a request to the office.
- [ ] Inbox with "received" and "sent" views per role; admin additionally "all".
- [ ] Thread: first message = original, replies appended; replying sets `Responded` and **always** notifies the other party; close request.
- [ ] Types Information, General, Support, Appointment.
- [ ] Admin can delete (soft) with confirmation.
- [ ] Legacy data: `Message` → message #1, `Response` → message #2.
