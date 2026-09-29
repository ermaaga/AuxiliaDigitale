# AuxiliaDigitale — Piano di porting da Auxilia (Blazor) a .NET 10 API + Next.js

> Documento vivo. Aggiornalo a fine di ogni sessione di lavoro: spunta i task, aggiorna **Stato corrente** e il registro sessioni in fondo.
> Ultima revisione: 2026-09-29.

---

## 1. Obiettivo e fonti di verità

Portare l'applicazione legacy **Auxilia** (Blazor Server, `../Auxilia`, HEAD `8fa6622` del 2026-06-23, tag `v.2.0.0`) su una nuova piattaforma:

- **Backend**: .NET 10, API REST versionate (`/api/v1`), modular monolith + Clean Architecture, multi-tenant (Catalog DB + DB per tenant), Worker Rebus/RabbitMQ, Redis/Valkey.
- **Frontend**: Next.js (App Router) + BFF, UI nuova (shadcn/ui, Tailwind v4), responsive, dark mode, WCAG 2.2 AA.
- **Regola d'oro**: nessuna funzionalità del legacy può andare persa. Il contratto è l'inventario `docs/parity/F01…F34`.

| Fonte | Cosa decide | Dove |
|---|---|---|
| Codice legacy | **Comportamento funzionale** (vince su tutto il resto in caso di dubbio) | `../Auxilia` |
| Blueprint | Architettura, stack, fasi, sicurezza, workflow | `~/.claude/plugins/marketplaces/auxilia-claude-skills/docs/BLUEPRINT.md` |
| Data model | Schema DB target (supera il blueprint §6.3) | `…/auxilia-claude-skills/docs/data-model.md` |
| Decisioni | Decisioni già prese (#1–#12) | `…/auxilia-claude-skills/docs/decisions.md` |
| Skill `auxilia-dev` | Regole operative per ogni tipo di task | plugin `auxilia-dev@auxilia-claude-skills` |
| Parità | Criteri di accettazione per funzionalità | `docs/parity/Fxx.md` |
| Anomalie legacy | Bug/stranezze del legacy e come trattarle | `docs/parity/legacy-quirks.md` |

> Nota: il blueprint chiama il monorepo `auxilia-next`. **Questo repository (`AuxiliaDigitale`) è quel monorepo** (decisione #1): stessa struttura `backend/`, `frontend/`, `deploy/`, `docs/`. Nei riferimenti delle skill leggere `auxilia-next` = `AuxiliaDigitale`.

---

## 2. Come usare il piano (ogni giornata di lavoro)

1. Apri questo file, sezione **Stato corrente**: prendi il primo task non completato le cui dipendenze sono chiuse.
2. Carica le skill: sempre `auxilia-architecture` + `auxilia-dependency-policy`; poi quelle indicate nel task (per le story: `auxilia-story`).
3. Leggi i file di parità `docs/parity/Fxx.md` citati dal task e le anomalie `Qxx` collegate.
4. Implementa con il workflow W3 (contract-first → backend → test → OpenAPI/api-client → frontend → E2E).
5. Chiusura: build e test verdi, spunta i criteri di accettazione nei `Fxx.md`, spunta il task qui, aggiorna **Stato corrente** e **Registro sessioni**, commit.

Dimensionamento: **1 task ≈ 1 giornata** (0,5 = mezza giornata; 1,5/2 = task da spezzare in due sessioni, stesso ID con suffisso `a`/`b` nel registro).

---

## 3. Stato corrente

- Fase in corso: **Fase 0 — non iniziata**
- Prossimo task: **P0-01**
- Decisioni bloccanti aperte: D-01, D-02, D-03 (vedi §5); proposta di architettura in revisione

---

## 4. Risultati dell'analisi del legacy (sintesi)

| Area | Legacy (numeri) |
|---|---|
| Entità EF | 29 (`Auxilia.Model/Entities`), 22 migrazioni, 6 seed script incrementali |
| Servizi | 23 con interfaccia + 12 senza + 1 background service + 1 handler Rebus |
| UI | 78 componenti Razor, 45 route (`/admin/*` 16, `/employee/*` 11, `/client/*` 4, `/system/*` 12, pubbliche 4) |
| Traduzioni | ~500 chiavi EN/IT in `LocalizationSeedData.cs` |
| Configurazioni | 6 chiavi `SystemConfiguration` di default + tema/background; `environmentconfig.json` (moduli per ruolo); `PageConfiguration` (visibilità pagine + colonne griglie) |
| Test | 28 test unitari (4 file) |

**Funzionalità trovate che il blueprint non elenca** (aggiunte all'inventario):
- **F33** — Template di cartelle per servizio (`MembershipFolderTemplate`): albero cartelle per Membership, documenti della pratica dentro le cartelle, spostamento documenti, **download ZIP** di una cartella o dell'intera pratica.
- **F34** — Comportamenti trasversali UI: sessione singola per utente (il nuovo login disconnette le altre sessioni), "ricorda username", toggle chiaro/scuro, nome app vs logo (`UseAppName`), sfondo login configurabile, KPI dashboard basati su campi custom (`CAF`, `PATRONATO`), polling notifiche, conferme, toast.

**Regole implicite importanti** estratte dal codice (dettagli nei `Fxx.md`):
- Stato attivo del cliente = esiste almeno una pratica `IsActive` con `EndDate` nulla o futura; la chiusura di una pratica imposta `EndDate = now` → il cliente diventa inattivo quando non ha più pratiche aperte (F05/F09).
- Visibilità pratiche/documenti per l'operatore (F10): la pratica è visibile se senza specializzazione, o con specializzazione non privata, o se l'operatore possiede quella specializzazione. **Il codice diverge dalla documentazione legacy** → decisione D-04.
- Operatore: può gestire (modificare/cancellare) solo pratiche senza specializzazione o con una sua specializzazione; cancellare solo se non concluse; crea pratiche solo su servizi senza specializzazione o con una sua (F10).
- Nuovo cliente senza operatore → assegnato all'operatore di default (creazione cliente, approvazione registrazione, creazione pratica).
- 60 anomalie/bug del legacy classificate in `docs/parity/legacy-quirks.md` (Q01–Q60), ciascuna con proposta *mantieni / correggi / decidi*.

---

## 5. Decisioni

### 5.1 Già prese (da `decisions.md`, non ridiscutere)
**D-09 (2026-09-29): schede allenamento e tutto il codice "palestra" rimossi** (F18) · #1 monorepo · #2 tenant nel path `/{tenantSlug}` · #3 PDF con PDFsharp-MigraDoc · #8 EN+IT · #10 vocabolario CRM (`Subscription`→`Case`/Pratica, `Membership`→`Service`, `MembershipType`→`ServiceCategory`, modulo `Billing`→`Cases`) · #11 estensioni CRM (timeline attività, task/scadenze, checklist documenti per servizio, pagamenti multipli) · #12 data model v1.

### 5.2 Da decidere (con default proposto)

| ID | Domanda | Default proposto | Serve entro |
|---|---|---|---|
| D-01 | Remote GitHub per questo repo (`ermaaga/AuxiliaDigitale`?) e branch protection | repo privato `ermaaga/AuxiliaDigitale`, `main` protetto, squash merge | P0-02 |
| D-02 | #6 blueprint: il runner crea i DB tenant (utente con `CREATEDB`)? | sì, con flag `--existing-database` | fine Fase 1 |
| D-03 | #4 blueprint: "token di sicurezza" = refresh token rotante + credenziale client app | confermato | inizio Fase 2 |
| D-04 | Semantica pratiche private (Q09): seguire il **codice** (chi possiede la specializzazione vede) o la **doc legacy/data-model** (solo operatore assegnato)? | seguire il codice (è ciò che gli utenti usano oggi) + admin vede tutto | P4 task 4.18 |
| D-05 | Login vs stato cliente (Q01): oggi l'utente "inattivo" può comunque fare login | separare: `users.is_active` (accesso) ≠ `client_profiles.status` (business); import legacy con accesso abilitato per tutti | Fase 2 |
| D-06 | Password iniziale (Q06): oggi `"password"`/`DefaultPassword` | link di attivazione via email (+ admin può impostare password manualmente come oggi) | 4.08 |
| D-07 | Scadenza pratica alla creazione (Q04): oggi `EndDate` resta nulla, `DurationDays` non usato | mantenere (null) + campo `due_on` facoltativo; nessuna scadenza automatica | 4.17 |
| D-08 | Sessione singola per utente (Q56) | mantenere come impostazione tenant, default ON | P2-04 |
| D-10 | Job scadenze: invio email automatico (oggi solo notifica) | notifica sempre + email se `SendExpiryEmail` (nuova impostazione, default OFF = parità) | P5-02 |
| D-11 | Slug e nome del tenant del cliente attuale | da chiedere | Fase 6 |
| D-12 | #5 hosting prod, #7 osservabilità, #9 mobile | aperte (blueprint) | Fase 7 |

---

## 6. Piano per fasi e task

Legenda colonne: **Stima** in giornate · **Dip.** = dipendenze · **F** = file di parità coperti · **Skill** = skill da caricare oltre alle due sempre obbligatorie.

### Fase 0 — Baseline e scheletro (≈ 4 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P0-01 | Congelamento legacy: tag `legacy-final-baseline` su `../Auxilia`; revisione con l'utente di `docs/parity/*` e `legacy-quirks.md`; chiusura D-01. | 0,5 | – | tutte | legacy-migration |
| [ ] P0-02 | Scheletro monorepo: `backend/` (`Auxilia.slnx`, `global.json` SDK 10, `Directory.Build.props` con nullable/warnings-as-errors/NuGetAudit, `Directory.Packages.props`, progetti vuoti §5.2 + test), `deploy/`, `.editorconfig`, `.gitignore`, `CLAUDE.md`, `.claude/settings.json` con marketplace `ermaaga/auxilia-claude-skills`. | 1 | P0-01 | F32 | architecture |
| [ ] P0-03 | Workspace frontend: pnpm, `apps/web` (Next.js App Router, TS strict), `packages/ui`, `packages/api-client`, `packages/config` (eslint, tsconfig, tailwind v4), init shadcn/ui. | 1 | P0-02 | – | frontend-feature, ui-design |
| [ ] P0-04 | CI GitHub Actions: build+test backend e frontend, NuGetAudit, nuget-license, `pnpm audit`, licenze, OSV-Scanner, gitleaks; Dependabot settimanale. | 1 | P0-03 | F32 | dependency-policy |
| [ ] P0-05 | `Auxilia.Architecture.Tests` (NetArchTest): dipendenze tra layer, pacchetti in blocklist, naming. | 0,5 | P0-02 | – | testing |

Uscita: pipeline verde sullo scheletro.

### Fase 1 — Fondamenta backend (≈ 13 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P1-01 | `SharedKernel`: `Result<T>`, `Error`/`ErrorType`, `Entity`/`AggregateRoot`, eventi di dominio, guard, `IdGenerator` (Guid v7) + unit test. | 1 | P0-05 | – | backend-feature |
| [ ] P1-02 | `Diagnostics`: `EventCodes` per range §7.2, `Log.*` con `[LoggerMessage]`, `Errors.*`, comando registry, test unicità/range. | 1 | P1-01 | F25 | log-codes |
| [ ] P1-03 | `ServiceDefaults` + `AppHost` Aspire (Postgres, Valkey, RabbitMQ, Api, Worker, Web); Serilog + OTel + enricher (`EventCode`, `TenantSlug`, `TraceId`…); `/health/live`, `/health/ready`. | 1 | P1-02 | F32 | architecture |
| [ ] P1-04 | `Auxilia.Api` host: Minimal API + `Asp.Versioning`, ProblemDetails con `errorCode`, `IExceptionHandler` (`AUX-10001`, concorrenza `AUX-10010`), OpenAPI + Scalar (dev), export OpenAPI in build + diff in CI, header di sicurezza. | 1 | P1-03 | F28, F29 | api-contract |
| [ ] P1-05 | Astrazioni applicative: `ICommand/IQuery` + handler, decorator Logging→Validation→Authorization→Transaction, registrazione Scrutor, paginazione standard (`page,pageSize,sort,filter[...],search`). | 1 | P1-04 | F28 | backend-feature |
| [ ] P1-06 | `Persistence.Catalog`: `tenants`, `tenant_domains`, `client_applications`, `platform_users`, `migration_runs`, `data_protection_keys` + migrazione; connection string cifrata (Data Protection). | 1 | P1-05 | – | multitenancy, ef-migration |
| [ ] P1-07 | Tenancy: `ITenantContext`, risoluzione claim→`X-Tenant`→host, mismatch 403 `AUX-11004`, tenant non attivo, `ITenantDbContextFactory` con cache; test di isolamento. | 1 | P1-06 | – | multitenancy, testing |
| [ ] P1-08 | `Persistence.Tenant` base: `TenantDbContext`, snake_case, schema per modulo, interceptor audit, `xmin`, soft delete, schema `ops` (`data_migrations_history`, `outbox_messages`, `processed_messages`, `legacy_id_map`, `number_sequences`), `IDataMigration` runner; `Persistence.Tests` (migra da zero, idempotenza ×2). | 1,5 | P1-07 | F29, F30 | ef-migration, data-migration |
| [ ] P1-09 | `auxctl` (MigrationRunner): `migrate catalog`, `migrate tenants [--tenant] [--parallel] [--dry-run]`, `tenant provision` (W4, stato `MigrationFailed`), seed iniziale, `diagnostics registry`. | 1,5 | P1-08, D-02 | F30, F32 | tenant-provisioning |
| [ ] P1-10 | Cache: HybridCache + Redis, chiavi `t:{slug}:…`, invalidazione per tag, degrado con Redis giù; `DistributedLock.Redis`. | 0,5 | P1-07 | – | caching |
| [ ] P1-11 | Messaging: Rebus + RabbitMQ (code §5.6), step tenant/correlation, outbox transazionale + dispatcher, handler idempotenti, retry + second-level + `error`; host `Auxilia.Worker` vuoto; test con Testcontainers. | 1,5 | P1-08 | F14, F19 | messaging-rebus |
| [ ] P1-12 | Email: porta `IEmailSender`, adapter MailKit, template Liquid (Fluid) EN/IT, coda `auxilia.email`, impostazioni SMTP lette dal tenant (password cifrata). | 1 | P1-11 | F23 | messaging-rebus, localization |

Uscita: `auxctl tenant provision` funzionante in locale.

### Fase 2 — Identità e sicurezza (≈ 7 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P2-01 | Schema `identity` (+ `directory.people` minimo per la FK), ASP.NET Identity, hasher composito (verifica BCrypt legacy → rehash), lockout, policy password; separazione accesso/stato cliente (D-05). | 1 | P1-09, D-05 | F01, F31 | security |
| [ ] P2-02 | JWT ES256 + rotazione chiavi + JWKS; `client_applications` + `X-Client-Id`; `refresh_sessions` con rotazione e rilevamento riuso; logout; deny-list `jti`. Endpoint login/refresh/logout/forgot/reset password. | 1,5 | P2-01, D-03 | F01, F17 | security |
| [ ] P2-03 | Permessi: costanti per modulo, `role_permissions` seed (mappa ruoli legacy), policy provider, autorizzazione resource-based; `GET /me`, `GET /me/navigation` (stub). | 1 | P2-02 | F22 | security, backend-feature |
| [ ] P2-04 | Rate limiting (login/registrazione per IP, per client app, per utente), eventi sicurezza `AUX-29xxx`, **sessione singola per utente** (D-08). | 0,5 | P2-02, D-08 | F17, F34 | security |
| [ ] P2-05 | SignalR `/hubs/notifications` con JWT, gruppi `t:{slug}:u:{id}` / `role`, backplane Redis, evento `ForceLogout`. | 1 | P2-02, P1-10 | F16, F17 | security |
| [ ] P2-06 | `auxctl users reset-password` (F31) + test di integrazione auth end-to-end (login, refresh, riuso, logout, cross-tenant, lockout). | 1 | P2-05 | F01, F31 | testing |

Uscita: login/refresh/logout testati end-to-end.

### Fase 3 — Fondamenta frontend + Localizzazione (≈ 9 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.01 | **Localizzazione BE** (anticipata perché serve alla shell): `languages`, `resource_keys`, `resource_translations`; data-migration con le ~500 chiavi di `LocalizationSeedData.cs`; `GET /i18n/{lang}` con ETag e cache per tag; CRUD chiavi/traduzioni con ricerca e "mancanti". | 1 | P2-03 | F24 | localization, data-migration |
| [ ] P3-01 | Design system: token CSS, componenti shadcn di base, `next-themes` (dark mode), font self-hosted, override branding tenant. | 1 | P0-03 | F23, F34 | ui-design |
| [ ] P3-02 | BFF auth: `/api/auth/login|logout|refresh`, sessione server-side in Redis, cookie `__Host-aux_sid`, proxy `/api/bff/[...path]` (Bearer, `X-Client-Id`, `X-Tenant`, refresh trasparente), CSRF, CSP con nonce; routing `[tenant]`. | 1,5 | P2-06 | F01 | security, frontend-feature |
| [ ] P3-03 | `packages/api-client` generato (openapi-typescript + openapi-fetch), React Query, gestione errori con codice `AUX-xxxxx` copiabile. | 0,5 | P3-02 | F28 | api-contract |
| [ ] P3-04 | i18n: `next-intl` con bundle dall'API (ETag) + fallback statico; switch lingua. | 0,5 | 4.01, P3-03 | F24 | localization |
| [ ] P3-05 | Shell: sidebar da `/me/navigation`, topbar, ⌘K (placeholder), campanella (placeholder), menu utente, tema/lingua; pagine pubbliche login (ricorda username, nome app/logo, sfondo), forgot/reset password; stati loading/empty/error. | 1,5 | P3-01, P3-04 | F01, F23, F34 | ui-design, frontend-feature |
| [ ] P3-06 | Kit componenti: `DataTable` (paginazione/ordinamento/filtri server-side, stato in URL con `nuqs`, colonne da layout, colonne campi custom con badge di gruppo, bottoni export, vista card mobile), form kit (rhf+zod), combobox ricercabile, dialog di conferma, toast `sonner`. | 1,5 | P3-03 | F20, F21, F26, F28, F34 | frontend-feature, ui-design |
| [ ] P3-07 | Test FE: Vitest + MSW, Playwright + axe, E2E smoke login per ruolo. | 0,5 | P3-05 | F01 | testing |
| [ ] 4.02 | **Localizzazione FE**: `/settings/localization` (ricerca, categorie, chiavi mancanti evidenziate, modifica inline, nuova chiave). | 1 | P3-06 | F24 | localization |

Uscita: login dal browser con tenant, shell tradotta.

### Fase 4 — Porting dei moduli (≈ 44,5 gg)

Ordine d'esecuzione = ordine della tabella. Ogni task BE include test unit + integrazione (permessi, isolamento tenant, errori); ogni task FE include E2E di parità + axe.

**Configuration (F20–F23)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.03 | BE impostazioni: `settings` (chiavi legacy + default), branding (tema gradient/solid, colori, sfondo gradient/colore/immagine, logo, `UseAppName`), endpoint pubblico branding per login, `email_settings` (password cifrata) + "invia email di prova". | 1 | 4.02, P1-12 | F23 | backend-feature |
| [ ] 4.04 | BE moduli/navigazione/permessi: registry `modules` (seed da `environmentconfig.json` + `PageConfiguration.IsEnabled` per ruolo → permessi), `GET /me/navigation`, modulo disabilitato → 404; editor permessi per ruolo. | 1,5 | 4.03 | F22 | security, data-migration |
| [ ] 4.05 | BE griglie e campi custom: `grid_layouts` (per griglia/ruolo), `user_saved_views`, `custom_field_definitions` (text/number/date/bool + gruppo + colore badge + visibile in griglia + mostra in dashboard), validazione server-side di `custom_fields`. | 1 | 4.04 | F20, F21 | schema-change |
| [ ] 4.06 | FE `/settings/*`: generali (toggle), branding, email, moduli/permessi, griglie, campi custom + renderer form dinamico. | 1,5 | 4.05 | F20–F23 | frontend-feature |

**Directory (F04–F06, F12)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.07 | BE clienti: `people`, `client_profiles`, `assignments` (storico); lista "miei"/"tutti" con filtri e ordinamenti legacy; dettaglio; creazione (regole admin vs operatore, CF regex+univocità, email=username, campi custom); modifica; soft delete; toggle stato (richiede operatore assegnato); assegna/rimuovi operatore; specializzazione cliente. | 1,5 | 4.05 | F05 | backend-feature, schema-change |
| [ ] 4.08 | BE operatori: lista con specializzazioni, creazione (attivazione D-06), dettaglio, modifica, impostazione password, operatore di default unico, attiva/disattiva, clienti assegnati + assegna/rimuovi, assegnazione ad amministratore, carico di lavoro. | 1 | 4.07, D-06 | F06 | backend-feature |
| [ ] 4.09 | BE specializzazioni: CRUD (ruolo Client/Employee, email, numero lavoro, privata), soft delete, assegna/rimuovi utenti del ruolo, elenco assegnati. | 0,5 | 4.07 | F12 | backend-feature |
| [ ] 4.10 | BE profilo: `GET/PUT /me/profile`, lingua, cambio password, immagine profilo (ridimensionata 400×400), preferenza tema, sessioni personali. | 1 | 4.07 | F04, F34 | backend-feature |
| [ ] 4.11 | FE clienti: `/clients` (viste "i miei"/"tutti"), wizard `/clients/new`, **Scheda 360°** (panoramica, dati, operatore, specializzazione, campi custom; tab successive vuote). | 1,5 | 4.07, P3-06 | F05 | frontend-feature, ui-design |
| [ ] 4.12 | FE operatori (`/employees`, dettaglio), specializzazioni (`/settings/specializations` + assegnazioni), `/profile`. | 1,5 | 4.08–4.10 | F04, F06, F12 | frontend-feature |

**Registrations (F02–F03)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.13 | BE registrazioni: endpoint pubblico con ALTCHA, gate `RegistrationEnabled`, validazioni legacy, duplicato pendente, notifica admin, email conferma se `SendRegistrationConfirmationEmail`; inbox pending/processed (Admin e Operatore); approva (crea persona+utente, operatore default, attivazione) / rifiuta con note; stato `Approved/Rejected`. | 1 | 4.08, P1-12 | F02, F03 | security, backend-feature |
| [ ] 4.14 | FE `/[tenant]/register` (lingua da `RegistrationLanguage`, messaggio se disabilitata, consenso privacy) + `/registrations`. | 1 | 4.13 | F02, F03 | frontend-feature |

**Cases — backend (F08–F10, F33 struttura)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.15 | BE catalogo: `service_categories` CRUD, `services` CRUD (prezzo, durata, attivo, categoria, specializzazione), dettaglio servizio con elenco pratiche; soft delete/disattivazione. | 1 | 4.09 | F08 | backend-feature |
| [ ] 4.16 | BE dominio pratica: aggregate `Case`, macchina a stati (avanti; indietro tranne da Inserted/Completed; Completed terminale), completamento con importo pagato + respinta, storico stati, `case_number`, pagamenti, regole di creazione (operatore default, prezzo snapshot, specializzazione), ricalcolo stato cliente. | 1,5 | 4.15, D-07 | F09, F05 | backend-feature, schema-change |
| [ ] 4.17 | BE query e autorizzazione pratiche: lista con filtri (cliente, servizio, escludi concluse, "solo mie specializzazioni"), ordinamenti legacy, pratiche per cliente; policy **pratiche private** (D-04), regole di gestione operatore; test dedicati F10. | 1 | 4.16, D-04 | F09, F10 | security, testing |
| [ ] 4.18 | BE template cartelle per servizio: albero (aggiungi radice/figlio, rinomina, riordina, elimina ricorsivo). | 0,5 | 4.15 | F33 | backend-feature |

**Documents (F14, F33 documenti)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.19 | BE storage: porta `IFileStorage`, adapter Local/FTP (FluentFTP)/Azure Blob, selezione provider come legacy, chiavi `tenants/{slug}/documents/…`, staging→commit, SHA-256, magic bytes, whitelist, dimensione max per tenant. | 1 | P1-11 | F14 | security |
| [ ] 4.20 | BE documenti: upload multipart multi-file (anno ≥ anno-10, area, descrizione, pratica, cartella, nome personalizzato con estensione, sanitizzazione, duplicati), lista (tutti/per cliente) con filtri e ordinamenti legacy + filtro accesso operatore, dettaglio, modifica, elimina, download; aree come lookup; spostamento cartella; **ZIP** cartella/pratica; percorso asincrono Worker + `DocumentProcessed`. | 1,5 | 4.19, 4.17, 4.18 | F14, F33, F10 | backend-feature, messaging-rebus |
| [ ] 4.21 | FE documenti: `/documents` e tab nella Scheda 360°, uploader (drag&drop, multiplo, incolla da appunti, progresso, controlli duplicati/dimensione), drawer dettaglio con anteprima e modifica, permessi di gestione. | 1,5 | 4.20 | F14 | frontend-feature |

**Cases — frontend**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.22 | FE pratiche: `/cases` (creazione rapida admin; toggle operatore "mostra tutte"/"mostra concluse"), dettaglio con timeline/progress, contenuto per stato (Inserita: dati servizio; In lavorazione: albero cartelle + uploader + sposta + ZIP; Inviata: riepilogo; Conclusa: importo/esito), dialog completamento, avanti/indietro con conferma; tab pratiche nella Scheda 360°. | 2 | 4.21 | F09, F10, F33 | frontend-feature, ui-design |
| [ ] 4.23 | FE servizi (`/services` + dettaglio con pratiche ed export) + categorie + editor template cartelle. | 1 | 4.22 | F08, F33 | frontend-feature |

**Scheduling (F13)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.24 | BE appuntamenti: creazione staff (default Approved, visibile nel calendario globale, durata, note, data futura) con notifica cliente; richiesta cliente (Pending, scelta operatore, notifica); modifica/approva/rifiuta/completa/annulla/elimina con notifiche; stati `Pending/Approved/Rejected/Completed/Cancelled`; liste per operatore/cliente/globale/tutti; storico; conflitti. | 1,5 | 4.07, P2-05 | F13 | backend-feature |
| [ ] 4.25 | FE `/appointments`: FullCalendar (mese/settimana/giorno/lista), drawer crea/gestisci, vista cliente con richiesta, drag&drop, tab nella Scheda 360°. | 1,5 | 4.24 | F13 | frontend-feature, ui-design |

**Engagement (F15–F17)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.26 | BE richieste a thread: creazione cliente (all'operatore assegnato o agli admin), creazione operatore (agli admin), tipi, liste ricevute/inviate secondo ruolo, risposta (messaggio + stato + notifica), chiusura, eliminazione admin. | 1 | 4.07 | F15 | backend-feature |
| [ ] 4.27 | BE notifiche: persistenza, lista paginata, non lette, segna letta/tutte, elimina, push `NotificationReceived`, deep link per tipo e ruolo, preferenze. | 1 | P2-05 | F16 | backend-feature |
| [ ] 4.28 | BE sessioni attive (admin): elenco con IP/UA/login/ultima attività/durata + KPI, forza logout. | 0,5 | P2-05 | F17 | security |
| [ ] 4.29 | FE `/requests` (inbox, thread, risposta, nuova richiesta con "chiedi al mio operatore"), centro notifiche real-time, `/sessions`, client SignalR (riconnessione, `ForceLogout`). | 1,5 | 4.26–4.28 | F15–F17, F34 | frontend-feature |

**Imports (F19)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.31 | BE import: tipi (Employee/Client/Service/Case, campi importabili e obbligatori, template Excel con intestazioni obbligatorie evidenziate), job (upload → Worker valida riga per riga → anteprima → conferma/annulla), progresso `ImportProgress`, eliminazione solo a job concluso. | 1,5 | P1-11, 4.16 | F19 | messaging-rebus, backend-feature |
| [ ] 4.32 | FE `/imports` wizard + tipi + dettaglio job con anteprima. | 1 | 4.31 | F19 | frontend-feature |

**Reporting e dashboard (F07, F26, F27)**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.33 | BE reporting: PDF (MigraDoc: griglia generica, overview clienti), CSV, Excel (ClosedXML); endpoint export per ogni lista che rispetta i filtri; export asincrono oltre soglia (`ExportReady`). | 1,5 | 4.07 | F07, F26 | backend-feature |
| [ ] 4.34 | BE dashboard per ruolo: KPI e grafici legacy (admin: pratiche per servizio, incassi per mese, appuntamenti; operatore: KPI da campi custom, appuntamenti per giorno/stato; cliente: pratica attiva, prossimi appuntamenti) + "da fare oggi". | 1 | 4.24, 4.26, 4.13 | F27, F34 | backend-feature |
| [ ] 4.35 | FE dashboard (recharts) + `/clients` vista overview con filtri ed export PDF + export su tutte le tabelle. | 1,5 | 4.34, 4.33 | F07, F26, F27 | frontend-feature |

**Audit/Logs ed estensioni CRM**

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] 4.36 | BE+FE log: sink `audit.app_logs` (Warning+ con codice evento), `entity_changes` via interceptor, API di ricerca (codice, livello, traceId, utente, data), pagina `/logs` con dettaglio. | 1 | P1-02 | F25 | log-codes |
| [ ] 4.37 | Estensioni CRM (decisione #11): timeline attività (eventi di dominio), task/scadenze con promemoria, checklist documenti per servizio/pratica. | 2 | 4.22, 4.25 | F09, F27 | backend-feature, frontend-feature |

### Fase 5 — Worker e job (≈ 3 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P5-01 | Job scadenze pratiche (ogni 6h, fan-out per tenant, lock, idempotente): gate `AutoSubscriptionExpiry`, soglia `SubscriptionExpiringDays`, disattiva scadute, aggiorna stato cliente, notifiche "scaduta"/"in scadenza" (una al giorno), email secondo D-10; azione manuale "invia avviso scadenza". | 1 | 4.17, 4.27, D-10 | F11 | messaging-rebus |
| [ ] P5-02 | Job promemoria task, pulizia file orfani in staging, export asincroni. | 1 | 4.37, 4.33 | F14, F26 | messaging-rebus |
| [ ] P5-03 | `Worker.IntegrationTests`: idempotenza, retry, error queue, job scadenze. | 1 | P5-02 | F11 | testing |

### Fase 6 — Migrazione dati `auxctl legacy import` (≈ 7 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P6-01 | `docs/migration/mapping.md` tabella per tabella + read model EF del DB legacy (sola lettura) + `legacy_id_map`. | 1 | Fase 4, D-11 | tutte | legacy-migration |
| [ ] P6-02 | Import utenti → `people` + `users` (BCrypt `LegacyBcrypt`) + profili + assegnazioni + ruoli + specializzazioni. | 1 | P6-01 | F01, F05, F06, F12 | legacy-migration |
| [ ] P6-03 | Import servizi/categorie/template cartelle/pratiche (+ pagamento, storico stato). | 1 | P6-02 | F08, F09, F33 | legacy-migration |
| [ ] P6-04 | Import documenti: copia file da Local/FTP/Azure con SHA-256, aree, cartelle. | 1 | P6-03 | F14, F33 | legacy-migration |
| [ ] P6-05 | Import appuntamenti, richieste→messaggi, notifiche, registrazioni, storico import, configurazioni (settings, email ricifrata, moduli/pagine→permessi, griglie, campi custom, traduzioni `is_customized`, tema/sfondo). | 1,5 | P6-04 | F13, F15–F17, F19–F24 | legacy-migration |
| [ ] P6-06 | Report di riconciliazione, `--dry-run`, `--since`, `LegacyImport.Tests` su dump anonimizzato. | 1,5 | P6-05 | tutte | legacy-migration, testing |

### Fase 7 — Hardening (≈ 5 gg)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P7-01 | Checklist OWASP ASVS L2, header, upload, segreti, rate limit, pen-test interno. | 1 | Fase 6 | – | security |
| [ ] P7-02 | Performance (indici, piani query, Lighthouse ≥ 90) + audit accessibilità su tutte le pagine. | 1 | Fase 6 | – | ui-design |
| [ ] P7-03 | Deploy: immagini API/Worker/Web/Runner, compose/hosting (D-12), osservabilità prod, backup, runbook. | 1,5 | D-12 | F32 | – |
| [ ] P7-04 | Suite E2E di regressione di parità completa per ruolo; verifica che ogni `Fxx.md` sia spuntato. | 1,5 | P7-01 | tutte | testing |

### Fase 8 — UAT, cutover, dismissione (≈ 3 gg + tempo utenti)

| ID | Task | Stima | Dip. | F | Skill |
|---|---|---|---|---|---|
| [ ] P8-01 | Tenant di staging migrato da dump reale; UAT per ruolo (Admin, Operatore, Cliente, Configuratore) sugli scenari di parità; difetti tracciati sui `Fxx`. | 1 | Fase 7 | tutte | legacy-migration |
| [ ] P8-02 | Prova generale di cutover + criteri oggettivi di rollback; go-live (legacy in sola lettura, import delta, riconciliazione, switch URL, monitoraggio per codice `AUX-*`). | 1 | P8-01 | – | legacy-migration |
| [ ] P8-03 | Dismissione: backup finale, archivio `AppLogs`, spegnimento, **rotazione credenziali legacy** (password Postgres in `appsettings.json`/`docker-compose.yml`, app-password SMTP in `DataSeeder.cs`, segreti FTP/Azure/reCAPTCHA). | 0,5 | P8-02 | F32 | security |

**Totale stimato: ≈ 88 giornate di lavoro** (Fase 0: 4 · F1: 13 · F2: 7 · F3: 9 · F4: 44,5 · F5: 3 · F6: 7 · F7: 5 · F8: 2,5). La Fase 4 si può parallelizzare per modulo dopo 4.07.

---

## 7. Tracciabilità funzionalità → task

| F | Funzionalità | Task |
|---|---|---|
| F01 | Login / logout | P2-01, P2-02, P2-06, P3-02, P3-05, P3-07 |
| F02 | Registrazione pubblica | 4.13, 4.14 |
| F03 | Approvazione registrazioni | 4.13, 4.14 |
| F04 | Profilo utente | 4.10, 4.12 |
| F05 | Gestione clienti | 4.07, 4.11, 4.16 |
| F06 | Gestione operatori | 4.08, 4.12 |
| F07 | Overview clienti + PDF | 4.33, 4.35 |
| F08 | Servizi e categorie | 4.15, 4.23 |
| F09 | Pratiche (workflow) | 4.16, 4.17, 4.22, 4.37 |
| F10 | Pratiche private | 4.17, 4.20, 4.22 |
| F11 | Scadenza pratiche | P5-01, P5-03 |
| F12 | Specializzazioni | 4.09, 4.12 |
| F13 | Appuntamenti | 4.24, 4.25 |
| F14 | Documenti | 4.19, 4.20, 4.21 |
| F15 | Richieste | 4.26, 4.29 |
| F16 | Notifiche | P2-05, 4.27, 4.29 |
| F17 | Sessioni attive | P2-02, P2-04, 4.28, 4.29 |
| F18 | ~~Schede allenamento~~ — **rimossa** (D-09) | – |
| F19 | Import dati | 4.31, 4.32 |
| F20 | Campi custom | 4.05, 4.06, P3-06 |
| F21 | Configurazione griglie | 4.05, 4.06, P3-06 |
| F22 | Abilitazione moduli/pagine | P2-03, 4.04, 4.06 |
| F23 | Configurazione sistema | 4.03, 4.06, P1-12 |
| F24 | Localizzazione | 4.01, 4.02, P3-04 |
| F25 | Log applicativi | P1-02, 4.36 |
| F26 | Export griglie | P3-06, 4.33, 4.35 |
| F27 | Dashboard | 4.34, 4.35 |
| F28 | Paginazione server-side | P1-05, P3-06 |
| F29 | Concorrenza ottimistica | P1-04, P1-08 |
| F30 | Seed incrementali | P1-08, P1-09 |
| F31 | Tool password | P2-06 |
| F32 | Docker / Aspire | P0-02, P0-04, P1-03, P7-03 |
| F33 | Template cartelle + ZIP | 4.18, 4.20, 4.22, 4.23 |
| F34 | Comportamenti trasversali UI | P2-04, P3-01, P3-05, P3-06, 4.10, 4.29, 4.34 |

---

## 8. Rischi principali

| Rischio | Mitigazione |
|---|---|
| Ambito ampio (multi-tenant + estensioni CRM) allunga i tempi | Le estensioni (4.37) sono isolate in fondo alla Fase 4; il go-live di parità può precederle se serve. |
| Divergenze di comportamento non notate | Ogni task parte dai `Fxx.md`; E2E di parità per ruolo; UAT con utenti reali (P8-01). |
| Semantiche ambigue del legacy (pratiche private, stato cliente) | Decisioni D-04/D-05 esplicite prima dei task relativi; test dedicati. |
| Qualità dati legacy (CF duplicati, username non univoci, `Area` libera) | Dry-run + report di riconciliazione; regole di pulizia documentate in `mapping.md`. |
| Credenziali in chiaro nel legacy | Nessun segreto copiato; rotazione in P8-03; gitleaks in CI dal giorno 1. |

---

## 9. Registro sessioni

| Data | Task | Esito | Note |
|---|---|---|---|
| 2026-09-29 | Analisi + piano | Completato | Letto tutto il legacy; creati `docs/PLAN.md`, `docs/parity/*`. |
