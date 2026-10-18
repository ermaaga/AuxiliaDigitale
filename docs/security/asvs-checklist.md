# Sicurezza — checklist ASVS L2 e pen-test interno (H-01)

Stato al 2026-10-06 (H-01), §3 al 2026-10-16 (C-08, N05). Livello richiesto: **OWASP ASVS 4.0.3 livello 2** (skill `auxilia-security`).
Ogni riga indica dove sta il controllo e quale test lo verifica; `⚠` = rischio residuo con il task che lo chiude.

## 1. Pen-test interno automatico

`backend/tests/Auxilia.Api.IntegrationTests/Host/SecurityPerimeterTests.cs` gira sull'host reale (tutti gli endpoint
registrati in `Program.cs`, oltre 300 combinazioni metodo × route) e fallisce quando un endpoint nuovo non dichiara la
propria protezione o risponde al chiamante sbagliato.

| Test | Cosa prova | Esito atteso |
|---|---|---|
| `EveryEndpoint_DeclaresHowItIsProtected` | ogni endpoint `/api/v1` ha `RequirePermission`, `RequireTenantUser`, `RequirePlatformUser`/`RequirePlatformTenant` oppure `AllowAnonymous` con la motivazione nell'elenco del test; nessun endpoint solo `RequireAuthorization()` | inventario pulito |
| `ProtectedEndpoints_WithoutAToken_Answer401` | ogni endpoint non anonimo senza token (con `X-Tenant`) | 401 |
| `TenantScopedPlatformToken_NeverReachesTenantUserEndpoints` | token di piattaforma del tenant su endpoint di business e dell'utente (D-21) | 403/404 |
| `ConsoleToken_NeverReachesTenantEndpoints_EvenWithATenantHeader` | token di console (senza tenant) + `X-Tenant` su endpoint del tenant e tecnici | 400/403/404 |
| `TenantAdministrator_NeverReachesPlatformEndpoints` | Administrator del tenant su console e endpoint tecnici (N02) | 403 |
| `TenantAdministrator_NeverReachesAnotherTenant` | token del tenant A con `X-Tenant` del tenant B | 403 `AUX-11004` |
| `RealtimeHub_RefusesTenantScopedPlatformTokens` | hub SignalR con token di piattaforma | connessione chiusa |

Le route ricevono valori casuali e corpi vuoti (multipart per gli upload, query obbligatorie dove servono al binding):
si verifica che la guardia risponda prima di qualsiasi logica.

### Rilievi e correzioni

| # | Rilievo | Gravità | Correzione |
|---|---|---|---|
| R1 | Un **token di console** (piattaforma, senza tenant) con l'header `X-Tenant` raggiungeva gli endpoint dell'utente del tenant protetti solo da `RequireAuthorization()` (`GET /me/navigation` → 200, `POST /auth/logout` → 204); lo stesso valeva per il token di piattaforma del tenant. Nessun dato di business esposto (le risposte erano vuote), ma il perimetro D-21 era aperto | Media | `RequireTenantUser()` (`Api/Authorization/TenantUserEndpointFilter.cs`): i token di piattaforma ricevono 403 `AUX-12071`; applicato a `/me/*`, `/users/{id}/image`, `/auth/logout`. L'inventario vieta d'ora in poi gli endpoint solo autenticati |
| R2 | L'hub `/hubs/notifications` accettava un token di piattaforma del tenant (ha `sub`, `sid`, `tenant`): la connessione entrava nel gruppo del tenant e riceveva gli eventi realtime destinati a tutti | Media | `NotificationsHub` rifiuta lo scope `platform` (motivo `PlatformToken`, `AUX-18002`) |
| R3 | Import Excel: il file `.xlsx` era controllato solo per estensione e poi aperto da ClosedXML nel Worker; un archivio compresso di 10 MB può espandersi a GB (zip bomb, esaurimento di memoria) | Media | `XlsxImportWorkbook.IsReasonablePackage`: firma ZIP, al massimo 1000 voci e 200 MB decompressi dichiarati (.NET rifiuta le voci che superano la dimensione dichiarata); altrimenti il file è "illeggibile" come prima |
| R4 | Le chiavi di Data Protection (cifrano connection string dei tenant, password SMTP, chiavi di firma, segreti TOTP) sono nel Catalog senza protezione a riposo: un dump del Catalog basta per decifrare i segreti | Alta in produzione | ⚠ **H-03**: `ProtectKeysWith…` dal secret store dell'hosting (D-12) — già previsto in `CatalogPersistence` |

## 2. Checklist per capitolo

| Capitolo ASVS | Controllo | Dove | Verifica |
|---|---|---|---|
| V1 Architettura | Modular monolith a livelli, il browser non vede mai i token (BFF), API unica per web/console/mobile | `docs/architecture/ARCHITECTURE.md` §14, `Auxilia.Architecture.Tests` | test di architettura |
| V2.1 Password | minimo 12 caratteri (impostazione `auth.password.minLength`, 8–128), storico, scadenza, un'unica `IPasswordPolicy` | `Application/Identity`, F35 | `AccountSecurityEndpointsTests` |
| V2.2 Anti-automazione | lockout progressivo (5 tentativi, 5 min raddoppiati fino a 24 h), rate limit per IP su sign-in, link account, registrazioni | `IdentitySettings`, `RateLimitingSetup` | `RateLimitingTests`, `AuthFlowTests` |
| V2.4 Archiviazione credenziali | PBKDF2 di ASP.NET Identity; BCrypt legacy solo in verifica con rehash al primo accesso | `Infrastructure/Security` | test di Identity |
| V2.5 Recupero | link monouso a scadenza breve, stessa risposta per utenti sconosciuti | `AuthEndpoints` | `AuthFlowTests` |
| V2.7/2.8 Secondo fattore | TOTP obbligatorio per gli utenti System (D-22); per gli utenti dei tenant app di autenticazione facoltativa, obbligatoria per ruolo (`auth.mfa.requiredRoles`), anti-replay, codice errato conteggiato nel lockout, reset che chiude le sessioni (N04); OTP via e-mail opzionale | `PlatformAuthManager`, `SessionManager.TwoFactor` | `PlatformIdentityTests`, `TwoFactorTests`, `TwoFactorEndpointsTests` |
| V3 Sessioni | access token ES256 brevi + refresh opachi ruotati a ogni uso, riuso → revoca della famiglia; deny-list `jti`/sessione; logout, cambio password e revoca da Administrator → `ForceLogout`; idle e assoluta configurabili | `AuthenticationSetup`, `Infrastructure/Security/Tokens` | `AuthEndpointsTests`, `ActiveSessionEndpointsTests`, `NotificationsHubTests` |
| V3.4 Cookie | `__Host-aux_sid`/`__Host-aux_psid`: Secure, HttpOnly, SameSite=Lax, Path=/ | `frontend/apps/web/src/lib/bff/session.ts` | `bff.test.ts` |
| V4.1 Controllo accessi | ogni endpoint dichiara permesso, utente del tenant, piattaforma o anonimo motivato; deny by default | `Api/Authorization` | `SecurityPerimeterTests` |
| V4.2 Accesso ai record | policy sulle risorse (`IResourceAccessPolicy`) applicate in SQL alle liste (F10, D-04) | `CaseAccessPolicy`, `AppointmentAccessPolicy`, … | test di integrazione dei moduli |
| V4 Multi-tenant | il claim `tenant` del token prevale; claim ≠ header/host → 403 `AUX-11004` + evento `AUX-29xxx` | `TenantResolutionMiddleware` | `TenantResolutionTests`, `SecurityPerimeterTests` |
| V4 Perimetro System | i token di piattaforma non raggiungono mai gli endpoint di business né dell'utente (D-21); gli utenti del tenant non raggiungono la console | `PlatformEndpointFilter`, `TenantUserEndpointFilter`, `NotificationsHub` | `SecurityPerimeterTests`, `PlatformIdentityTests` |
| V4.2.2 CSRF | BFF: SameSite=Lax + header `X-Requested-With: auxilia` + controllo `Origin` sulle mutazioni | `lib/bff/csrf.ts` | `bff.test.ts` |
| V5 Validazione | validazione lato server in ogni Manager (errori di campo con chiavi di traduzione), DTO senza mass assignment, nessun SQL concatenato (EF Core) | `Application/*` | test dei Manager |
| V5.3 Output | API solo JSON/ProblemDetails; anteprima dei template e-mail in `iframe sandbox=""`; nessun `dangerouslySetInnerHTML` | `features/marketing` | revisione |
| V6 Crittografia | ES256 con rotazione (`auxctl keys rotate`, JWKS); segreti cifrati con Data Protection | `catalog.signing_keys` | ⚠ R4 (H-03) |
| V7 Log | codici evento univoci, eventi di sicurezza 29xxx (accessi falliti, lockout, riuso refresh, cross-tenant, revoche, rate limit); nessuna password né token nei log | `Auxilia.Diagnostics`, `docs/log-event-registry.md` | test del registro |
| V8 Dati | log degli accessi per tenant, export con permessi della lista, file solo attraverso l'API autorizzata | `Documents`, `Reporting` | test dei moduli |
| V9 Comunicazioni | HSTS (API fuori da Development, web sempre con `preload`), `upgrade-insecure-requests` | `Program.cs`, `next.config.ts` | ⚠ TLS terminato dall'hosting (H-03) |
| V10 Codice malevolo | allowlist delle dipendenze, lock file, audit moderate+, licenze, gitleaks, osv-scanner in CI | `.github/workflows/ci.yml`, ADR 0011 | CI |
| V12 File | documenti: whitelist dei tipi + magic bytes + dimensione + SHA-256, staging → commit, controllo nel Worker, chiavi `tenants/{slug}/…` (altre rifiutate); immagini profilo decodificate e ricodificate; branding PNG/JPEG/WebP per firma (mai SVG); import `.xlsx` ≤ 10 MB con controllo del pacchetto (R3); anteprima (ADR 0020): PDF disegnati da PDF.js su canvas (niente frame, niente script/XFA del PDF, worker servito dall'app con CSP propria `default-src 'self'; script-src 'self' 'wasm-unsafe-eval'`), testo in `<pre>`, file aperti in una scheda con la CSP dell'API inoltrata dal BFF | `Documents.Public.IFileStore`, `IImageProcessor`, `BrandingAsset`, `XlsxImportWorkbook` | test di dominio e di Infrastructure |
| V13 API | header `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, CSP `default-src 'none'`, `Cache-Control: no-store`, nessun header `Server`; OpenAPI e Scalar solo in Development; 429 con `Retry-After` | `SecurityHeaders`, `RateLimitingSetup` | `ErrorResponseTests`, `RateLimitingTests` |
| V14 Configurazione | segreti solo da user-secrets/ambiente (`.env.example` vuoti, password di default solo negli stack locali), CSP per richiesta con nonce e `strict-dynamic` (`worker-src 'self'`), `X-Forwarded-For` accettato solo dai proxy fidati (ultimo hop) | `proxy.ts`, `ForwardedHeadersSetup` | `ForwardedHeadersTests` |

## 3. Account collegati, posta e calendari esterni (N05, C-08)

Modulo `mailbox` (ADR 0021): l'API legge per conto dell'utente caselle e calendari di terzi. Nota privacy e valutazione
preliminare della DPIA: [`privacy-connected-accounts.md`](privacy-connected-accounts.md).

| Capitolo ASVS | Controllo | Dove | Verifica |
|---|---|---|---|
| V4.2 Accesso ai record | account e copie visibili solo al proprietario: ogni query filtra per l'utente del token, un id di un altro utente o di un altro tenant risponde 404, una sincronizzazione chiesta da altri non tocca gli account del proprietario; Administrator e System non vedono la posta degli altri | `ConnectedAccountPolicy`, `MailboxAccessResolver`, query `…Data` per utente | `MailboxIsolationTests` (altro utente e altro tenant su tutti gli endpoint), `SecurityPerimeterTests` (token di piattaforma) |
| V4.1 Ruoli | permessi `mailbox.accounts.connect`/`mail.view`/`mail.file` solo per Administrator ed Employee; i clienti ricevono 404 dal filtro del modulo | `MailboxPermissions`, `MailboxModule` | `Clients_*` dei test di integrazione |
| V2.10 / V6.4 Segreti di servizio | refresh token OAuth e password IMAP cifrati con Data Protection con il tenant nello scopo (`IConnectedAccountSecretProtector`), mai restituiti né registrati; refresh ruotato dal fornitore salvato insieme al suo uso; `ToString()` di accesso e login senza segreti | `Infrastructure/Security`, `SyncPorts` | `ConnectedAccountTests`, `SyncTests` · ⚠ R4 (chiavi a riposo, H-03) |
| V3.5 OAuth | authorization code + PKCE S256, `state` monouso di 10 minuti salvato come hash, legato all'utente che l'ha chiesto, verifier protetto lato server; l'API è client confidenziale e il browser non vede mai i token (callback attraverso il BFF); `id_token` verificato per `aud`; scope in sola lettura e incrementali | `OAuthProviderBase`, `ConnectedAccountManager`, `connectedAccountCallback` | `OAuthProviderTests`, `ConnectedAccountEndpointsTests`, E2E `tenant-connected-accounts.spec.ts` |
| V12.6 SSRF | server IMAP scritti dall'utente: solo nomi DNS, ogni risposta DNS controllata (privati, loopback, link-local, metadata cloud rifiutati, evento `AUX-29036`), porte da `mailbox.imap.allowedPorts`, connessione all'indirizzo controllato (nessuna seconda risoluzione) e di nuovo a ogni parte della sincronizzazione; i link dei fornitori seguiti solo sui loro host | `OutboundAddressPolicy`, `ImapConnectionTester`, `MailSynchronizer`, adapter Graph | `ConnectedAccountEndpointsTests`, `MailSyncTests`, `CalendarProviderTests` |
| V9.2 TLS verso i server | IMAP solo `SslOnConnect`/`StartTls`, validazione del certificato e del nome mai disattivata (`ImapCertificateTrust.System`), HTTPS verso Google e Microsoft | `Adapters/Mail/Imap` | `ImapServerTests` (GreenMail: certificato sconosciuto o nome diverso → connessione rifiutata) |
| V5.3 / V12.5 Contenuti di terzi | corpo HTML solo in un `iframe srcdoc` sandbox senza script né same-origin, immagini remote bloccate dalla CSP; `.eml` e allegati scaricati con `Content-Disposition: attachment`, `no-store`, tipo generico per gli allegati; originale letto in un file temporaneo limitato a 50 MB; contenuti della posta trattati come dati, mai come istruzioni | `mail-reader.tsx`, `MailEndpoints`, `TemporaryFiles` | `MailFilingEndpointsTests`, `MailReadingTests`, vitest `mail.test.ts` |
| V12.1 File archiviati | "Inserisci in pratica" passa dagli stessi controlli dei documenti (`.eml` controllato sulle intestazioni MIME, allegati per tipo e magic bytes, dimensione, regole F10 della pratica) | `Documents.Public.ICaseDocuments` | `MailFilingEndpointsTests` |
| V11 Logica / anti-automazione | sincronizzazione solo su richiesta, rate limit `mailbox-sync` per utente, un run attivo per account e scope, intervallo minimo, budget di tempo con continuazione | `AccountSyncRequester`, `RateLimitingSetup` | `SyncTests`, `MailboxSyncEndpointsTests` |
| V7 Log | solo codici e id (mai indirizzi, oggetti, contenuti); eventi di sicurezza `AUX-29033…29036` (collegato, scollegato, `state` rifiutato, host rifiutato) | `Log.Mailbox`, `Log.Security` | test del registro |
| V8.3 Dati personali | solo intestazioni ed eventi della finestra, con lunghezze massime; scollegando (o eliminando l'operatore) si cancellano segreti e copie e si revoca il consenso dove il fornitore lo prevede | `ConnectedAccountManager`, `ConnectedAccountCleanup` | `MailboxSyncEndpointsTests`, `MailboxIsolationTests`, E2E `tenant-mailbox.spec.ts` |

Rilievo di C-08: eliminare un operatore (soft delete della persona) lasciava i suoi account collegati con segreti e
copie. Corretto con `Mailbox.Public.IConnectedAccountCleanup`, chiamata da `EmployeeManager.DeleteAsync` nella stessa
operazione (revoca presso il fornitore dopo il commit). Resta voluto che la sola disattivazione dell'accesso li conservi.

## 4. Rischi residui (fuori da H-01)

- **R4 — chiavi di Data Protection a riposo** e **TLS/segreti di produzione**: H-03, con la decisione D-12 sull'hosting.
- `ForwardedHeaders:KnownProxies`/`KnownNetworks` vanno impostati per il reverse proxy di produzione (H-03), altrimenti
  il rate limit per IP vede l'indirizzo del proxy.
- `Captcha:Altcha:Key` va impostata in produzione (senza, la chiave è casuale per nodo e lo avvisa `AUX-29xxx` all'avvio).
- Credenziali del legacy (Postgres, SMTP, FTP/Azure, reCAPTCHA): rotazione al cutover, R-03.
- Antivirus sui documenti (ClamAV) non previsto: il controllo di tipo e contenuto resta l'unica difesa (decisione da
  prendere con l'hosting).
- N05: verifica Google degli ambiti Gmail (restricted) prima di uscire dalla modalità di test; prova manuale con
  account Google e Microsoft reali (le chiavi OAuth le registra l'utente, runbook `connected-accounts-oauth.md`); DPIA
  da decidere con l'utente (nota privacy §4).
