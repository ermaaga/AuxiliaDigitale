# Runbook — OAuth apps for connected accounts (N05)

Who: the owner of the deployment (not a tenant). When: before C-02 can be tried with real accounts, and once per new
web origin. Design: ADR 0021.

The redirect URIs below use `{web}` = the public origin of the web app (e.g. `https://app.example.com`; for local
development `http://localhost:3000`). Register **every** origin you use.

## Google Cloud (Gmail + Google Calendar)
1. https://console.cloud.google.com → new project (e.g. `auxilia-connected-accounts`).
2. *APIs & Services → Library*: enable **Gmail API** and **Google Calendar API**.
3. *Google Auth Platform → Branding*: app name, support e-mail, logo, app home page, privacy policy and terms links,
   authorised domain (the domain of `{web}`).
4. *Audience*: **External**, status **Testing**; add the test users (max 100) who will try it.
5. *Data access → Add scopes*: `openid`, `.../auth/userinfo.email`,
   `https://www.googleapis.com/auth/gmail.readonly` (restricted),
   `https://www.googleapis.com/auth/calendar.events.readonly` (sensitive).
6. *Clients → Create client*: type **Web application**; *Authorised redirect URIs*:
   `{web}/api/connected-accounts/callback/google` (one line per origin). Copy **Client ID** and **Client secret**.
7. Production (later): *Verification centre* → submit the app. `gmail.readonly` needs the restricted-scope verification
   **and** an annual third-party security assessment (CASA); calendar-only needs the standard verification. In Testing
   mode refresh tokens expire after 7 days (users reconnect).

## Microsoft Entra (Outlook / Microsoft 365 mail + calendar)
1. https://entra.microsoft.com → *Identity → Applications → App registrations → New registration*.
2. Name `Auxilia connected accounts`; *Supported account types*: **Accounts in any organizational directory and
   personal Microsoft accounts**; *Redirect URI*: platform **Web**, `{web}/api/connected-accounts/callback/microsoft`
   (add the other origins later under *Authentication*).
3. *Certificates & secrets → New client secret* (24 months max; note the expiry in the calendar). Copy the **Value**
   now (it is shown once) and the **Application (client) ID** from *Overview*.
4. *API permissions → Add → Microsoft Graph → Delegated*: `openid`, `email`, `offline_access`, `User.Read`,
   `Mail.Read`, `Calendars.Read`. Do not grant admin consent for other tenants: each customer's administrator decides.
5. Production (later): *Branding & properties* → **publisher verification** (Microsoft Partner Network id), otherwise
   work accounts see an "unverified" warning and many organisations block the consent.

## Configuration of the API
Never in `appsettings*.json` committed to git. Local development (user-secrets id `auxilia-api`, shared with the
Worker):

```bash
cd backend
dotnet user-secrets set ConnectedAccounts:CallbackBaseUrl "http://localhost:3000" --project src/Auxilia.Api
dotnet user-secrets set ConnectedAccounts:Google:ClientId "<client id>" --project src/Auxilia.Api
dotnet user-secrets set ConnectedAccounts:Google:ClientSecret "<client secret>" --project src/Auxilia.Api
dotnet user-secrets set ConnectedAccounts:Microsoft:ClientId "<application id>" --project src/Auxilia.Api
dotnet user-secrets set ConnectedAccounts:Microsoft:ClientSecret "<secret value>" --project src/Auxilia.Api
```

Deployed environments: the same keys as environment variables (`ConnectedAccounts__Google__ClientId`, …) from the
key vault, for **both** API and Worker. A provider without its keys is not offered in the profile.

## Rotation
- Microsoft client secret: create the new one, update the key vault, restart API and Worker, delete the old secret.
- Google client secret: *Clients → (client) → Add secret*, same steps, then disable the old one.
- Users' refresh tokens are not affected by a client-secret rotation.
