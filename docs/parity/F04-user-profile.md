# F04 — User profile

Status: [x] done (verified in H-04, 2026-10-09) · Tasks: B-03, B-05 · Quirks: Q35

## Legacy behaviour
- `Components/Profile.razor` (`/profile`, any authenticated user; header menu "MyProfile").
- Editable: FullName, Surname, Email, Phone, Language (active languages). Username shown read-only.
- Change password: new + confirm (must match), no current-password check; hashed with BCrypt.
- Profile image: upload image, resized client-side to JPEG max 400×400, max 2 MB, stored as base64 data URI in `User.ProfileImage`; delete image with confirmation. Image is shown in header, grids and detail pages.
- Changing language updates the session claims and UI language immediately ("LanguageChanged" toast).
- Header shows profile image or initials.

## Acceptance criteria
- [x] User can edit first name, last name, e-mail, phone; username is read-only. (B-03 API: `GET/PUT /me/profile`; B-05 page `/{tenant}/profile`)
- [x] Changing language switches UI language immediately and persists for next logins. (B-03 API: `PUT /me/language`, active tenant languages only; B-05: profile page and topbar switch save it, applied again after every sign-in)
- [x] Change password requires current password + confirmation and policy; other sessions optionally revoked. (API `POST /me/password` since P2-08: current password + policy, other sessions always closed; B-05: profile form with confirmation and the tenant rules)
- [x] Upload profile image (jpg/png/webp), server-side resize to ≤400×400; delete with confirmation; image visible in header and lists. (B-03 API: `PUT/DELETE /me/image`, SkiaSharp resize to JPEG, `GET /users/{id}/image` with ETag for the owner and staff; B-05: upload/remove with confirmation, header, employee and client lists and details through `imageVersion`)
- [x] Theme preference (system/light/dark) persisted (Q35). (B-03 API: `PUT /me/preferences`, `identity.users.theme`; B-05: profile and topbar save it, applied after sign-in)
- [x] "My sessions": list own active sessions and revoke them. (B-03 API: `GET /me/sessions`, `DELETE /me/sessions/{id}`; page in B-21)
