# F04 — User profile

Status: [ ] not started · Tasks: B-03, B-05 · Quirks: Q35

## Legacy behaviour
- `Components/Profile.razor` (`/profile`, any authenticated user; header menu "MyProfile").
- Editable: FullName, Surname, Email, Phone, Language (active languages). Username shown read-only.
- Change password: new + confirm (must match), no current-password check; hashed with BCrypt.
- Profile image: upload image, resized client-side to JPEG max 400×400, max 2 MB, stored as base64 data URI in `User.ProfileImage`; delete image with confirmation. Image is shown in header, grids and detail pages.
- Changing language updates the session claims and UI language immediately ("LanguageChanged" toast).
- Header shows profile image or initials.

## Acceptance criteria
- [ ] User can edit first name, last name, e-mail, phone; username is read-only.
- [ ] Changing language switches UI language immediately and persists for next logins.
- [ ] Change password requires current password + confirmation and policy; other sessions optionally revoked.
- [ ] Upload profile image (jpg/png/webp), server-side resize to ≤400×400; delete with confirmation; image visible in header and lists.
- [ ] Theme preference (system/light/dark) persisted (Q35).
- [ ] "My sessions": list own active sessions and revoke them.
