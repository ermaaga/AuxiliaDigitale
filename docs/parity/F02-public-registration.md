# F02 — Public registration

Status: [x] done (API only, B-06; no pages by D-14) · Tasks: B-06 · Quirks: Q05, Q08, Q53, Q54, Q59

> **Decision D-14:** API only, **no pages**. The submission endpoint is called by a registered external client application (captcha verifier pluggable per client app). `registration.enabled` defaults to **false**; admin notification is behind `registration.notifyAdmins` (default false) until an approval page exists. UI-related criteria (page, language forcing, closed message) apply to the API responses/e-mails only.

## Legacy behaviour
- `Components/Register.razor` (`/register`, anonymous). If setting `RegistrationEnabled` (default `True`) is false → "RegisterNotAllowed / TemporaryUnabled" message, no form.
- Page language forced to setting `RegistrationLanguage` (`en`/`it`, default `it` from `SystemConfigToggles` defaults).
- Fields and validation (`RegistrationModel`): first name & last name required, 2–50 chars, letters/spaces/accents only; e-mail required, valid, ≤100; phone required 8–20 chars `^[\d\s\+\-\(\)]+$`; birth date required, 1900-01-01..2008-12-31 ("at least 16"); fiscal code required, exactly 16 alphanumerics; privacy consent checkbox (submit disabled until checked, value not stored, privacy link empty — Q08).
- reCAPTCHA v2/v3 when appsettings `ReCaptcha:Enabled` (server verification in `RegistrationRequestService.ValidateRecaptchaAsync`).
- Normalization: names trimmed, e-mail trimmed + lower-case, fiscal code trimmed + upper-case.
- `CreateRegistrationRequestAsync`: rejects if a **pending** request with the same e-mail exists (also DB unique index on e-mail, Q05); stores `RequestDate` UTC; creates a notification (type `RegistrationRequest`, title "Nuova Richiesta di Registrazione") for **every Administrator**; sends confirmation e-mail "Richiesta di Registrazione Ricevuta" when setting `SendRegistrationConfirmationEmail` (default `False` in toggles, `true` fallback in code) — e-mail errors ignored.
- Success message: "Registrazione inviata con successo! Riceverai una conferma via email."; failure: "Email già presente o errore del sistema".

## Business rules
1. Registration can be switched off per tenant.
2. Duplicate pending request for the same e-mail is refused.
3. All administrators are notified of each new request.
4. Confirmation e-mail only when the setting is on.

## Acceptance criteria
- [x] Given `registration.enabled=false`, the API rejects the captcha request and the submissions with `403` `AUX-13037` (the external application shows "registration closed"; no page, D-14).
- [x] Given `registration.defaultLanguage=it`, the e-mails go out in Italian unless the request names another active tenant language (no page to render, D-14).
- [x] Each validation rule above is enforced server-side with localized messages (person rules Q53/Q54, every field required, age ≥ `registration.minimumAge` Q59; `AUX-13042` with field errors). Client-side checks belong to the external application.
- [x] Given a pending request for `a@b.it`, when a new request with `A@B.it ` arrives, then it is rejected with a clear message (`409` `AUX-13038`); an e-mail already registered is `AUX-13039`.
- [x] Given a valid submission, then, when `registration.notifyAdmins` is on, every Administrator is notified in real time (`RegistrationRequested`; persisted notifications arrive with B-19).
- [x] Given `registration.sendConfirmationEmail=true`, then the applicant receives the confirmation e-mail (template `registration-received` EN/IT, through Messaging); failures are logged by the dispatcher and don't fail the request.
- [x] Privacy consent is mandatory and stored (timestamp + version).
- [x] Captcha (ALTCHA, `Ixnas.AltchaNet`, per client application; `none` only for confidential clients) verified server-side, once per solution; rate limit per IP (10/h).

## Improvements
ALTCHA instead of reCAPTCHA; uniqueness only among pending (Q05); consent stored; clearer errors.
