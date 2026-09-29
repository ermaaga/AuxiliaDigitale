# F23 — System configuration (settings, e-mail, theme, background)

Status: [ ] not started · Tasks: 4.03, 4.06, P1-12 · Quirks: Q46

## Legacy behaviour
`/system/configurations` (SystemConfigurator) with four sections + Refresh:
1. **E-mail** (`EmailConfigSection`, single `EmailConfiguration` row): SMTP server, port, SSL, username, password (show/hide), from e-mail, from name; save. Used by `EmailService` (first active row); password stored in clear.
2. **Background** (`BackgroundConfigSection`): type gradient (start/end colors → `BackgroundGradient` CSS), solid color (`BackgroundColor`), image (upload, resized 1920×1080 JPEG ≤2 MB, base64 in `BackgroundImage` + written to `wwwroot/backgrounds/background.jpg`); used on login page (30-min cache).
3. **Theme** (`ThemeConfigSection`): type gradient (primary/secondary, default #667eea/#764ba2) or solid; live preview; applied to card headers/buttons everywhere (`ThemeService`).
4. **Toggles** (`SystemConfigToggles`): ensures defaults exist — `RegistrationEnabled=True`, `RegistrationLanguage=it`, `SendRegistrationConfirmationEmail=False`, `AutoSubscriptionExpiry=False`, `UseAppName=True`, `SubscriptionExpiringDays=7` — and lists **all** `SystemConfiguration` rows (key, value editor by kind: language select / checkbox for boolean-like keys / number for `*Days|*Count|*Limit` / text, description, last update).
App-level config (appsettings): `AppName` ("Auxilia Digitale"), `DefaultPassword`, `SessionTimeout`, storage, queue, reCAPTCHA, cache, logging.

## Acceptance criteria
- [ ] `/settings/general`: all legacy keys with typed editors and descriptions; defaults seeded per tenant; generic list of other keys.
- [ ] `/settings/email`: SMTP host/port/security/user/password (encrypted, never returned in clear), from e-mail/name, active; "send test e-mail".
- [ ] `/settings/branding`: app name vs logo (`UseAppName`), logo upload, theme primary/secondary or solid (design tokens), login background gradient/color/image with preview.
- [ ] Branding applied to the whole UI and to the public login/register pages (public branding endpoint).
- [ ] Legacy values imported (SMTP password re-encrypted).
