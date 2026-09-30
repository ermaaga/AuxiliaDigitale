# AuxiliaDigitale — Piano di lavoro (v2)

> Documento vivo: a fine sessione spunta i task, aggiorna **Stato corrente** e **Registro sessioni**.
> v2 (2026-09-29): integrate le decisioni D-04…D-20 ([`decisions.md`](decisions.md)) e l'architettura v0.2 ([`architecture/ARCHITECTURE.md`](architecture/ARCHITECTURE.md)).

---

## 1. Obiettivo e fonti

Rifare **Auxilia** (Blazor Server, `../Auxilia`; baseline D-30 = `develop` @ `8fa6622` + branch `Security_Update` + `fix/zip-download-folder`) come piattaforma multi-tenant per **clienti, pratiche, appuntamenti e campagne marketing**: API .NET 10 + Next.js (app tenant + console di piattaforma), Worker su code, configurazione nel DB servita da Redis.

| Fonte | Decide | Dove |
|---|---|---|
| Codice legacy | comportamento funzionale | `../Auxilia` |
| Decisioni del progetto | scelte con l'utente (prevalgono) | `docs/decisions.md` |
| Architettura | struttura, DB, plug-in, config, log | `docs/architecture/ARCHITECTURE.md` |
| Parità legacy | criteri di accettazione F01–F35 | `docs/parity/` |
| Nuove funzionalità | criteri di accettazione N01–N03 | `docs/requirements/` |
| Blueprint / data-model / skill | regole tecniche generali | marketplace `auxilia-claude-skills` |

Questo repo è il monorepo che il blueprint chiama `auxilia-next`.

---

## 2. Come lavorare ogni giorno
1. Leggi **Stato corrente**, prendi il primo task aperto con dipendenze chiuse.
2. Carica le skill `auxilia-architecture` + `auxilia-dependency-policy` + quelle del task.
3. Leggi i file `Fxx`/`Nxx` citati e le decisioni collegate.
4. Workflow W3: contratto → backend → test → OpenAPI/api-client → frontend → E2E.
5. Chiudi: build/test verdi, criteri spuntati, task spuntato, stato e registro aggiornati, commit.

1 task ≈ 1 giornata (0,5 mezza; ≥1,5 in più sessioni).

---

## 3. Stato corrente
- Fase: **Fase 2 — in corso** (Fasi 0 e 1 completate; P2-01, P2-02 completati)
- Prossimo task: **P2-03** (permessi per modulo, `role_permissions` seed, policy resource-based, `/me`, `/me/navigation`)
- Architettura approvata; decisioni tutte chiuse tranne D-11 (tenant del cliente attuale, Fase 7) e D-12 (hosting/osservabilità, Fase 8)

---

## 4. Sintesi dell'analisi del legacy
29 entità, 22 migrazioni, 37 servizi, 78 componenti Razor, 45 route, ~500 chiavi di traduzione, 28 test. Aggiunte all'inventario due aree non presenti nel blueprint: **F33** (cartelle per servizio + ZIP) e **F34** (comportamenti trasversali UI). 60 anomalie del legacy classificate in `docs/parity/legacy-quirks.md`. Regole implicite chiave: stato cliente dalle pratiche (Q03), pratiche private come nel codice (D-04), operatore di default per nuovi clienti.
Fuori perimetro per decisione: schede allenamento (D-09), pagine di registrazione (D-14, solo API), job schedulati (D-15), login Google (D-19, predisposto).

---

## 5. Decisioni
Vedi [`decisions.md`](decisions.md). Aperte solo **D-11** (tenant del cliente attuale, entro Fase 7) e **D-12** (hosting/osservabilità, entro Fase 8).

---

## 6. Fasi e task

Colonne: **Stima** (giorni) · **Dip.** · **F/N** (parità / nuove funzionalità) · **Skill** (oltre alle due sempre obbligatorie).

### Fase 0 — Baseline e scheletro (≈ 4 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [x] P0-01 | Tag `legacy-final-baseline` su `../Auxilia`; conferma creazione remote GitHub (D-01); ADR iniziali in `docs/adr/` (monorepo, multi-tenant, ruolo System, config nel DB, log su file, niente job schedulati, Manager + OperationRunner). | 0,5 | – | tutte | legacy-migration |
| [x] P0-02 | Scheletro backend (`Auxilia.slnx`, `global.json`, `Directory.Build.props/Packages.props`, progetti §3 architettura, test), `deploy/`, `.editorconfig`, `.gitignore`, `.claude/settings.json` (marketplace), `CLAUDE.md` con comandi. | 1 | P0-01 | F32 | architecture |
| [x] P0-03 | Workspace frontend pnpm: `apps/web`, `packages/{ui,api-client,config}`, Tailwind v4, shadcn/ui. | 1 | P0-02 | – | frontend-feature, ui-design |
| [x] P0-04 | CI: build+test, NuGetAudit, licenze, `pnpm audit`, OSV-Scanner, gitleaks, Dependabot. | 1 | P0-03 | F32 | dependency-policy |
| [x] P0-05 | `Architecture.Tests`: layer, confini tra moduli, blocklist, naming. | 0,5 | P0-02 | – | testing |

### Fase 1 — Fondamenta backend (≈ 15 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [x] P1-01 | `SharedKernel`: Result, Error, Entity/AggregateRoot, eventi, guard, Guid v7 + test. | 1 | P0-05 | – | backend-feature |
| [x] P1-02 | `Diagnostics`: EventCodes (19000 Marketing, 25000 Messaging), `Log.*`, `Errors.*`, registro generato, test unicità/range. | 1 | P1-01 | F25 | log-codes |
| [x] P1-03 | `ServiceDefaults` + orchestrazione locale Docker Compose (niente Aspire, D-32); Serilog: console JSON + **file giornalieri per tenant** (`logs/tenants/{slug}/…`, `logs/platform/…`) su storage `local-file`/`azure-blob`, Information+, mascheramento, **livello minimo per tenant a tempo** (D-28); OTel predisposto; health. | 1,5 | P1-02 | F25, F32 | log-codes, observability |
| [x] P1-04 | Host API: versioning, ProblemDetails+`errorCode`, `IExceptionHandler`, OpenAPI+Scalar, export OpenAPI in CI, header sicurezza. | 1 | P1-03 | F28, F29 | api-contract |
| [x] P1-05 | Base applicativa (D-26) + **gate di coverage ≥ 80% Domain/Application** (`coverlet.MTP`, ADR 0014): convenzioni Manager/QueryService/Api pubblica, `IOperationRunner` (traccia, scope di log, transazione+outbox, metriche, log di esito con codice, mappatura eccezioni), catalogo `Operations.*`, `ICurrentUser`, validazione, contratto di paginazione, registrazione DI + test. | 1 | P1-04 | F28 | backend-feature, log-codes |
| [x] P1-06 | `Persistence.Catalog`: tenants, domini, **modules, plans, plan_modules, tenant_plans, tenant_module_overrides**, platform_users, platform_settings, client_applications, migration_runs, chiavi DP; seed piano `standard`. | 1,5 | P1-05 | F22, N02 | multitenancy, ef-migration |
| [x] P1-07 | Tenancy: `ITenantContext`, risoluzione (claim/header/host), 403 cross-tenant, `ITenantDbContextFactory`, test isolamento. | 1 | P1-06 | – | multitenancy |
| [x] P1-08 | `Persistence.Tenant` base: schemi, audit interceptor (`actor_type`), `xmin`, soft delete, `ops.*` (incl. `job_runs`), `IDataMigration` runner, Persistence.Tests. | 1,5 | P1-07 | F29, F30 | ef-migration, data-migration |
| [x] P1-09 | `auxctl`: migrate catalog/tenants, tenant provision/suspend/archive, diagnostics registry, `jobs run`. | 1,5 | P1-08, D-02 | F30, F32, N02 | tenant-provisioning |
| [x] P1-10 | Impostazioni: `SettingDefinition<T>`, 5 livelli, `ISettingsProvider`, `ReferenceDataCache<T>` (HybridCache + Redis + PUBLISH invalidazione L1), segreti cifrati. | 1 | P1-08 | F23 | caching |
| [x] P1-11 | Moduli: `IModuleDescriptor`, registry, sincronizzazione `catalog.modules`, calcolo moduli effettivi (piano ∩ override ∩ ruolo) in cache, filtro endpoint 404, builder navigazione. | 1 | P1-06, P1-10 | F22, N02 | architecture |
| [x] P1-12 | Rebus + RabbitMQ: code, outbox, handler idempotenti, retry, `error`; Worker (solo code); registry `IRecurringJob` + esecuzione manuale con lock e `ops.job_runs` (nessuno scheduler). | 1,5 | P1-08 | F11 | messaging-rebus |
| [x] P1-13 | `Messaging`: `IMessageChannel`, `messaging_accounts`, `sender_rules` (scopo × ruolo), risoluzione account, adapter `smtp` (MailKit), template Liquid EN/IT, `outbound_messages`, invio di prova; canale WhatsApp solo nel modello. | 1,5 | P1-12, P1-10 | F23, N03 | messaging-rebus, localization |

### Fase 2 — Identità e sicurezza (≈ 9 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [x] P2-01 | Schema `identity` (+ `directory.people` minimo), Identity, hasher BCrypt legacy → rehash, lockout, policy; accesso ≠ stato cliente (D-05); ruoli Administrator/Employee/Client. | 1 | P1-09 | F01, F31 | security |
| [x] P2-02 | JWT ES256 + JWKS, client app, refresh rotante con riuso, logout, deny-list; attivazione account e reset password via email (D-06). | 1,5 | P2-01, P1-13, D-03 | F01, F17 | security |
| [ ] P2-03 | Permessi per modulo, `role_permissions` seed, policy resource-based, `/me`, `/me/navigation`. | 1 | P2-02, P1-11 | F22 | security |
| [ ] P2-04 | Rate limiting, eventi sicurezza 29xxx, sessione singola come impostazione (default off, D-08). | 0,5 | P2-02 | F17, F34 | security |
| [ ] P2-05 | SignalR `/hubs/notifications`, gruppi per tenant/utente/ruolo, backplane Redis, `ForceLogout`. | 1 | P2-02 | F16, F17 | security |
| [ ] P2-06 | Identità di piattaforma: utenti System nel Catalog con 2FA TOTP obbligatoria (D-22), login console, scelta tenant → token di piattaforma (`act`), perimetro solo tecnico (D-21), audit. | 1,5 | P2-03 | N02 | security, multitenancy |
| [ ] P2-08 | Sicurezza account (F35): policy password da impostazioni, storico password, scadenza con cambio obbligatorio, OTP via email come metodo `email-otp`, tabella `identity.login_attempts` per ogni tentativo. | 1,5 | P2-02, P1-13 | F35 | security |
| [ ] P2-07 | Framework metodi di login (`IAuthenticationMethod`, `/auth/methods`, grant `external_code` progettato), `auxctl users reset-password`, test integrazione auth (login, refresh, riuso, cross-tenant, token di piattaforma). | 1 | P2-06 | F01, F31 | security, testing |

### Fase 3 — Fondamenta frontend + localizzazione (≈ 9 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] P3-01 | Localizzazione BE: lingue, chiavi, traduzioni; data-migration con le ~500 chiavi legacy (senza quelle palestra); `GET /i18n/{lang}` con ETag; API di gestione. | 1 | P2-03 | F24 | localization, data-migration |
| [ ] P3-02 | Design system: token, shadcn, dark mode, font, branding tenant. | 1 | P0-03 | F23, F34 | ui-design |
| [ ] P3-03 | BFF: sessioni separate app tenant / console, proxy con `X-Client-Id`/`X-Tenant`, refresh, CSRF, CSP. | 1,5 | P2-07 | F01, N02 | security, frontend-feature |
| [ ] P3-04 | `api-client` generato + React Query + errori con codice AUX. | 0,5 | P3-03 | F28 | api-contract |
| [ ] P3-05 | i18n `next-intl` dall'API + fallback. | 0,5 | P3-01, P3-04 | F24 | localization |
| [ ] P3-06 | Shell app tenant (navigazione da API, topbar, notifiche placeholder, tema/lingua) + pagine pubbliche: login (ricorda username, nome app/logo, sfondo), forgot/reset, **attivazione**. | 1,5 | P3-02, P3-05 | F01, F23, F34 | ui-design, frontend-feature |
| [ ] P3-07 | Kit: DataTable server-side (URL state, colonne da layout, colonne campi custom, export, card mobile), form kit, combobox, conferme, toast. | 1,5 | P3-04 | F20, F21, F26, F28, F34 | frontend-feature |
| [ ] P3-08 | Shell console di piattaforma: login System, elenco e selettore tenant. | 1 | P3-06 | N02 | frontend-feature |
| [ ] P3-09 | Vitest + MSW, Playwright + axe, E2E login (tenant e console). | 0,5 | P3-08 | F01 | testing |

### Fase 4 — Console di piattaforma (System) (≈ 11 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] S-01 | Tenant: elenco, creazione (provisioning asincrono), modifica, sospensione, archiviazione (nessuna eliminazione, D-25); piano e override moduli per ruolo. BE+FE. | 2 | P3-08, P1-11 | F22, N02 | tenant-provisioning |
| [ ] S-02 | Impostazioni tipizzate e branding per tenant. FE (+ endpoint). | 1 | S-01, P1-10 | F23 | frontend-feature |
| [ ] S-03 | Account di invio (SMTP; WhatsApp predisposto) e regole scopo × ruolo, invio di prova. BE+FE. | 1 | S-01, P1-13 | F23, N03 | frontend-feature |
| [ ] S-04 | Layout griglie per ruolo + definizioni campi custom (gruppo, colore, visibile in griglia, contatore dashboard), validazione server-side. BE+FE. | 1,5 | S-01 | F20, F21 | backend-feature, frontend-feature |
| [ ] S-05 | Editor etichette/traduzioni (ricerca, mancanti, modifica inline, nuove chiavi). FE. | 1 | P3-01, S-01 | F24 | localization |
| [ ] S-06 | Permessi dei ruoli + specializzazioni (CRUD, privata, assegnazioni). BE+FE. | 1 | S-01 | F12, F22 | security |
| [ ] S-07 | Visualizzatore log per tenant (API che legge i file giornalieri con filtri + pagina) + **livello di log temporaneo per tenant** (D-28). | 1 | P1-03, S-01 | F25 | log-codes |
| [ ] S-08 | Import: tipi (Employee/Client/Service/Case, template Excel), job (upload → Worker valida → anteprima → conferma/annulla), progresso real-time. BE+FE. | 2,5 | S-01, B-08 | F19 | messaging-rebus |

> S-08 dipende dal modulo Pratiche: eseguirlo dopo B-08 anche se è nella console.

### Fase 5 — Moduli di business, app tenant (≈ 32,5 gg)

**Anagrafiche (F04–F06)**
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] B-01 | BE clienti: people, client_profiles, assignments; liste mie/tutti con filtri/ordinamenti legacy; dettaglio; creazione (Admin attivo / Operatore non attivo e assegnato; CF regex+univoco; email=username; campi custom; attivazione D-06); modifica; soft delete; stato cliente; assegna operatore; specializzazioni cliente; tag. | 1,5 | P2-03, S-04 | F05, N01 | backend-feature, schema-change |
| [ ] B-02 | BE operatori: lista, creazione, dettaglio, modifica, password/attivazione, default unico, attivo, clienti assegnati, assegnazione ad amministratore, carico di lavoro. | 1 | B-01 | F06 | backend-feature |
| [ ] B-03 | BE profilo: dati, lingua, cambio password, immagine, preferenze (tema), sessioni personali. | 1 | B-01 | F04, F34 | backend-feature |
| [ ] B-04 | FE clienti: `/clients` (mie/tutti), wizard nuovo cliente, Scheda 360° (panoramica, dati, operatore, specializzazioni, tag, consensi, campi custom). | 1,5 | B-01, P3-07 | F05 | frontend-feature, ui-design |
| [ ] B-05 | FE operatori + profilo. | 1 | B-02, B-03 | F04, F06 | frontend-feature |
| [ ] B-06 | API registrazione esterna (D-14): invio da client app registrata (captcha pluggable), duplicati, conferma via email; elenco/approva/rifiuta per Admin/Operatore; **nessuna pagina**. | 1 | B-02, P1-13 | F02, F03 | security, backend-feature |

**Pratiche (F08–F10, F33)**
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] B-07 | BE catalogo: categorie, servizi (prezzo, durata, attivo, categoria, specializzazione), dettaglio servizio con pratiche; disattivazione. | 1 | S-06 | F08 | backend-feature |
| [ ] B-08 | BE dominio pratica: stati (avanti/indietro, Completed terminale), completamento con importo/esito, storico, numero pratica, pagamenti, regole di creazione, ricalcolo stato cliente. | 1,5 | B-07, D-07 | F05, F09 | backend-feature, schema-change |
| [ ] B-09 | BE query/autorizzazione pratiche: filtri, ordinamenti, "mostra tutte"/"mostra concluse", pratiche private (D-04), regole di gestione operatore; test dedicati. | 1 | B-08 | F09, F10 | security, testing |
| [ ] B-10 | BE cartelle per servizio (albero, rinomina, riordina, elimina ricorsivo). | 0,5 | B-07 | F33 | backend-feature |

**Documenti (F14, F33)**
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] B-11 | BE storage: `IFileStorage` Local/FTP/Azure, prefisso tenant, staging→commit, SHA-256, magic bytes, whitelist, dimensione max. | 1 | P1-12 | F14 | security |
| [ ] B-12 | BE documenti: upload multiplo con metadati e regole legacy, liste con filtri e accesso operatore, dettaglio, modifica, elimina, download, aree, sposta cartella, ZIP, post-elaborazione in coda. | 1,5 | B-11, B-09, B-10 | F10, F14, F33 | backend-feature, messaging-rebus |
| [ ] B-13 | FE documenti: pagina + tab 360°, uploader (drag&drop, multiplo, incolla, progresso), drawer dettaglio/anteprima. | 1,5 | B-12 | F14 | frontend-feature |
| [ ] B-14 | FE pratiche: lista (creazione rapida, toggle operatore), dettaglio con timeline e contenuto per stato, dialog completamento, avanti/indietro, tab 360°. | 2 | B-13 | F09, F10, F33 | frontend-feature, ui-design |
| [ ] B-15 | FE servizi + categorie + editor cartelle. | 1 | B-14 | F08, F33 | frontend-feature |

**Agenda e relazione (F13, F15–F17)**
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] B-16 | BE appuntamenti: creazione staff/richiesta cliente, modifica, approva/rifiuta/completa/annulla/elimina con notifiche, stati, liste, storico, conflitti. | 1,5 | B-01, P2-05 | F13 | backend-feature |
| [ ] B-17 | FE calendario (mese/settimana/giorno/lista), drawer, vista cliente, drag&drop, tab 360°. | 1,5 | B-16 | F13 | frontend-feature |
| [ ] B-18 | BE richieste a thread (cliente → operatore assegnato o ufficio; operatore → ufficio), risposte con notifica, chiusura. | 1 | B-01 | F15 | backend-feature |
| [ ] B-19 | BE notifiche: persistenza, liste, letto/tutte, elimina, push, deep link per tipo e ruolo, preferenze. | 1 | P2-05 | F16 | backend-feature |
| [ ] B-20 | BE sessioni attive (Admin): elenco + forza logout. | 0,5 | P2-05 | F17 | security |
| [ ] B-21 | FE richieste, centro notifiche, sessioni, client SignalR. | 1,5 | B-18–B-20 | F15–F17, F34 | frontend-feature |
| [ ] B-27 | Pagina Admin "Audit accessi" (filtri, ordinamento, export) + cambio password dal profilo e cambio forzato alla scadenza + login OTP nella pagina di login. BE query + FE. | 1 | P2-08, B-05 | F35 | frontend-feature |

**Report, dashboard, estensioni**
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] B-22 | BE export (PDF MigraDoc, CSV, Excel) per tutte le liste con filtri; overview clienti PDF; export grandi in coda. | 1,5 | B-01 | F07, F26 | backend-feature |
| [ ] B-23 | BE dashboard per ruolo (KPI e grafici legacy, contatori da campi custom, "oggi"). | 1 | B-16, B-18, B-09 | F27, F34 | backend-feature |
| [ ] B-24 | FE dashboard + overview clienti con export + export su tutte le tabelle. | 1,5 | B-23, B-22 | F07, F26, F27 | frontend-feature |
| [ ] B-25 | Job `cases.expiry` come comando (scadute → inattive, stato cliente, notifiche "scaduta/in scadenza" 1/giorno) eseguibile a mano dal System + azione "invia avviso scadenza" sulla pratica. | 0,5 | B-09, B-19, P1-12 | F11 | messaging-rebus |
| [ ] B-26 | Estensioni CRM: timeline attività, task/scadenze (senza promemoria automatici), checklist documenti per servizio/pratica. | 2 | B-14, B-17 | F09, F27 | backend-feature, frontend-feature |

### Fase 6 — Marketing (≈ 6 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] M-01 | Consensi (storico per canale/finalità) e tag: BE + UI nella Scheda 360° e nell'import. | 1 | B-04 | N01 | backend-feature, frontend-feature |
| [ ] M-02 | Segmenti dinamici (regola validata → query sicura, anteprima conteggio) e liste statiche (da selezione clienti o import). BE. | 1,5 | M-01, B-09 | N01 | backend-feature |
| [ ] M-03 | Template email, campagne (Draft → Sending → Sent/Cancelled/Failed), "invia ora" in coda, snapshot destinatari, esclusione senza consenso/soppressi, invio a lotti via Messaging (scopo Marketing × ruolo). BE. (Disiscrizione rimandata, D-24.) | 1,5 | M-02, P1-13 | N01, N03 | messaging-rebus |
| [ ] M-04 | FE marketing: segment builder con conteggio, liste, editor template con anteprima e invio di prova, wizard campagna, esiti. | 2 | M-03 | N01 | frontend-feature, ui-design |

### Fase 7 — Migrazione dati `auxctl legacy import` (≈ 7 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] E-01 | `docs/migration/mapping.md` + read model legacy + `legacy_id_map`; esclusioni: `WorkoutPlans`, utente/ruolo SystemConfigurator, `UserSessions`, `AppLogs`. | 1 | Fase 6, D-11 | tutte | legacy-migration |
| [ ] E-02 | Utenti → people/users (BCrypt) + profili + assegnazioni + ruoli + specializzazioni + storico password, `password_changed_at`, audit accessi (se il DB di produzione ha lo schema `Security_Update`). | 1 | E-01 | F01, F05, F06, F12, F35 | legacy-migration |
| [ ] E-03 | Servizi, categorie, cartelle, pratiche (+ pagamento, storico). | 1 | E-02 | F08, F09, F33 | legacy-migration |
| [ ] E-04 | Documenti (copia file con SHA-256), aree, cartelle. | 1 | E-03 | F14, F33 | legacy-migration |
| [ ] E-05 | Appuntamenti, richieste → messaggi, notifiche, registrazioni, storico import; configurazione → Catalog/tenant (impostazioni, SMTP → account di default ricifrato, pagine/moduli → override e permessi, griglie, campi custom, traduzioni `is_customized`, branding); consenso marketing email = true con fonte `LegacyMigration` (D-23). | 1,5 | E-04 | F13, F15–F17, F19–F24, N01 | legacy-migration |
| [ ] E-06 | Riconciliazione, `--dry-run`, `--since`, `LegacyImport.Tests`. | 1,5 | E-05 | tutte | legacy-migration, testing |

### Fase 8 — Hardening (≈ 5 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] H-01 | ASVS L2, header, upload, segreti, rate limit, perimetro System, pen-test interno. | 1 | Fase 7 | – | security |
| [ ] H-02 | Performance (indici, query, Lighthouse ≥ 90) + accessibilità. | 1 | Fase 7 | – | ui-design |
| [ ] H-03 | Deploy (immagini, hosting D-12), storage log con lifecycle policy, backup, runbook. | 1,5 | D-12 | F25, F32 | – |
| [ ] H-04 | Regressione E2E di parità per ruolo; verifica di tutti i `Fxx`/`Nxx`. | 1,5 | H-01 | tutte | testing |

### Fase 9 — UAT, cutover, dismissione (≈ 2,5 gg + tempo utenti)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] R-01 | Tenant di staging da dump reale; UAT per ruolo (Admin, Operatore, Cliente, System). | 1 | Fase 8 | tutte | legacy-migration |
| [ ] R-02 | Prova di cutover, criteri di rollback, go-live. | 1 | R-01 | – | legacy-migration |
| [ ] R-03 | Dismissione legacy: backup, archivio, rotazione credenziali (Postgres in `appsettings`/`docker-compose`, SMTP in `DataSeeder`, FTP/Azure/reCAPTCHA). | 0,5 | R-02 | F32 | security |

**Totale ≈ 100,5 giornate** (F0 4 · F1 15 · F2 9 · F3 9 · F4 11 · F5 32,5 · F6 6 · F7 7 · F8 5 · F9 2,5). Le Fasi 4 e 5 si possono parallelizzare dopo B-01.

---

## 7. Tracciabilità

| F/N | Funzionalità | Task |
|---|---|---|
| F01 | Login / logout | P2-01, P2-02, P2-07, P3-03, P3-06, P3-09 |
| F02 | Registrazione pubblica → **solo API** (D-14) | B-06 |
| F03 | Approvazione registrazioni → **solo API** (D-14) | B-06 |
| F04 | Profilo | B-03, B-05 |
| F05 | Clienti | B-01, B-04, B-08 |
| F06 | Operatori | B-02, B-05 |
| F07 | Overview clienti + PDF | B-22, B-24 |
| F08 | Servizi e categorie | B-07, B-15 |
| F09 | Pratiche | B-08, B-09, B-14, B-26 |
| F10 | Pratiche private | B-09, B-12, B-14 |
| F11 | Scadenze pratiche → **comando manuale** (D-15) | P1-12, B-25 |
| F12 | Specializzazioni | S-06 |
| F13 | Appuntamenti | B-16, B-17 |
| F14 | Documenti | B-11, B-12, B-13 |
| F15 | Richieste | B-18, B-21 |
| F16 | Notifiche | P2-05, B-19, B-21 |
| F17 | Sessioni | P2-02, P2-04, B-20, B-21 |
| F18 | ~~Schede allenamento~~ — rimossa (D-09) | – |
| F19 | Import | S-08 |
| F20 | Campi custom | S-04, P3-07 |
| F21 | Griglie | S-04, P3-07 |
| F22 | Moduli per ruolo | P1-06, P1-11, P2-03, S-01 |
| F23 | Configurazione sistema | P1-10, P1-13, S-02, S-03 |
| F24 | Localizzazione | P3-01, P3-05, S-05 |
| F25 | Log | P1-02, P1-03, S-07, H-03 |
| F26 | Export | P3-07, B-22, B-24 |
| F27 | Dashboard | B-23, B-24, B-26 |
| F28 | Paginazione | P1-05, P3-07 |
| F29 | Concorrenza | P1-04, P1-08 |
| F30 | Seed incrementali | P1-08, P1-09 |
| F31 | Tool password | P2-07 |
| F32 | Docker / Aspire | P0-02, P0-04, P1-03, H-03, R-03 |
| F33 | Cartelle + ZIP | B-10, B-12, B-14, B-15 |
| F34 | UI trasversale | P2-04, P3-02, P3-06, P3-07, B-03, B-21, B-23 |
| F35 | Sicurezza account (branch `Security_Update`) | P2-02, P2-08, B-27, E-02 |
| N01 | Marketing | B-01, M-01…M-04, E-05 |
| N02 | Piattaforma, tenant, piani e moduli | P1-06, P1-09, P1-11, P2-06, P3-03, P3-08, S-01 |
| N03 | Comunicazioni (account per ruolo) | P1-13, S-03, M-03 |

---

## 8. Rischi
| Rischio | Mitigazione |
|---|---|
| Ambito ampio | fasi con uscite verificabili; marketing ed estensioni CRM isolate |
| Divergenze di comportamento non viste | `Fxx` come contratto, E2E di parità, UAT |
| Invii marketing senza link di disiscrizione (D-24) | revoca consenso da parte dello staff; attivare la disiscrizione self-service prima di campagne verso grandi volumi |
| Log su file senza pulizia interna | lifecycle policy sullo storage (H-03), come da D-17 |
| Nessun job schedulato: attività periodiche dimenticate | registry dei job + pagina "Job" nella console con ultima esecuzione (`ops.job_runs`) |
| Credenziali in chiaro nel legacy | mai copiate, rotazione in R-03, gitleaks in CI |

---

## 9. Registro sessioni
| Data | Task | Esito | Note |
|---|---|---|---|
| 2026-09-29 | Analisi + piano v1 | Completato | Inventario F01–F34, anomalie Q01–Q60 |
| 2026-09-29 | Decisioni + architettura v0.2 + piano v2 | Completato | D-04…D-20; console System, Messaging, Marketing, log su file per tenant |
| 2026-09-29 | Chiusura decisioni | Completato | D-21…D-29, default D-01/02/03/07; Manager + OperationRunner; architettura approvata |
| 2026-09-29 | Skill marketplace | Completato | PR ermaaga/auxilia-claude-skills#2 (merge `3142d49`): skill allineate a D-01…D-29 |
| 2026-09-29 | P0-01 | Completato | Tag locali sul legacy (`legacy-final-baseline` @ `8fa6622`, `legacy-baseline-security-update` @ `e314e9a`, `legacy-baseline-zip-fix` @ `abf49b0`); baseline D-30 con F35; D-31; ADR 0001–0010; remote `origin` verificato |
| 2026-09-29 | P0-02 | Completato | Scheletro backend: `Auxilia.slnx` (12 progetti src + 8 test), `global.json` SDK 10.0.401, build con warning come errori e NuGetAudit, CPM, test xUnit v3 (Microsoft.Testing.Platform), `.editorconfig`, `.gitignore`, `.claude/settings.json`, `deploy/`. AppHost rimandato a P1-03. SDK arm64 installato in `~/.dotnet` |
| 2026-09-29 | P0-03 | Completato | Workspace pnpm: `apps/web` Next.js 16.3.7 (App Router, TS strict, Turbopack), `@auxilia/ui` (token chiaro/scuro, Tailwind v4, shadcn button), `@auxilia/api-client` (openapi-fetch, schema segnaposto), `@auxilia/config` (tsconfig, prettier); Node 24 LTS (`.nvmrc`, engine-strict); lint/typecheck/build/format/audit verdi. Licenze da decidere: LGPL-3.0 (`@img/sharp-libvips`, via next), CC-BY-4.0 (`caniuse-lite`), BlueOak/Python-2.0/CC0 (tool) |
| 2026-09-29 | P0-04 | Completato | CI GitHub Actions (backend, frontend, gitleaks CLI, osv-scanner CLI), action fissate per SHA, Dependabot, lock file NuGet, gate licenze .NET/pnpm con eccezioni approvate (ADR 0011); prima esecuzione verde (PR #3) |
| 2026-09-29 | P0-05 | Completato | 240 test di architettura: grafo dei riferimenti tra progetti, dipendenze tra layer (NetArchTest), confini tra moduli via `Public`, Manager/QueryService sealed e non pubblici, niente handler CQRS, niente BackgroundService nel Worker, pacchetti in blocklist nei lock file; verificato con 6 violazioni iniettate (tutte rilevate). Protezione di `main` non disponibile sul piano GitHub gratuito (repo privato): attivi solo squash merge e cancellazione branch |
| 2026-09-29 | P1-01 | Completato | SharedKernel: `Error` (codice AUX, tipo, dettagli di validazione), `Result`/`Result<T>` (Match, Map, BindAsync), `Entity`, `AggregateRoot` con eventi di dominio, `IdGenerator` Guid v7 (anche da `TimeProvider`); guard = helper BCL (`ThrowIfNull`…); 27 test. CA1716 disattivata (solo C#) |
| 2026-09-29 | P1-02 | Completato | `Auxilia.Diagnostics`: 20 range `EventCodes` con `[EventCodeRange]` (Marketing 19000, Messaging 25000; bus Rebus 23000 = `Bus`), codici di base 10001, 10010–10014, 11004; `Log.*` con `[LoggerMessage]`, `Errors.*`; `EventRegistry` + `auxctl diagnostics registry` → `docs/log-event-registry.md`; 12 test in `EventCodeTests` (range fissi e allineati, unicità, codici nel range, `EventName`, un log per codice, factory nel proprio range, niente log a stringa, registro allineato), verificati con violazioni iniettate. I log a stringa sono bloccati anche in compilazione da CA1848. Rimandati: `Errors.Host.PreconditionFailed` (412, `ErrorType` non ha un tipo per 412: si decide in P1-04) e catalogo `Operations.*` (P1-05) |
| 2026-09-29 | P1-03 | Completato | **Niente Aspire** (D-32, ADR 0013): tutti gli `Aspire.Hosting*` dipendono da json-everything con EULA "Open Source Maintenance Fee" → blocklist; infrastruttura locale con `deploy/compose.dev.yml` (Postgres 18, Valkey 8, RabbitMQ 4). `ServiceDefaults` (→ Diagnostics): Serilog con console JSON compatto + `TenantFileSink` a lotti su file giornalieri per tenant (`local-file` / `azure-blob` append blob, slug non valido → `platform`), buffer locale e `AUX-10015`/`10016` se lo storage cade, `EventCode` da `EventId`, mascheramento segreti e CF, `TenantLogLevels` (D-28: override per tenant a scadenza, level switch sul minimo attivo); OpenTelemetry (sorgente/meter `Auxilia`, OTLP solo se `OTEL_EXPORTER_OTLP_ENDPOINT`), `/health/live` e `/health/ready`, resilienza HTTP standard. Api e Worker collegati e provati (stesso file `logs/platform/…`). 332 test (+ `Auxilia.ServiceDefaults.Tests`, test di health). Rimandati: controlli di salute delle dipendenze (P1-06/P1-10/P1-12, schema tenant P1-09), log del Runner (P1-09), identità gestita per il blob (serve `Azure.Identity`, fuori allowlist: con D-12) |
| 2026-09-29 | P1-04 | Completato | Host API: versioning `/api/v{version}` (Asp.Versioning.Http, gruppo `MapApiV1`, `api-supported-versions`; versione ignota → 404); ProblemDetails RFC 9457 con `errorCode`, `type` `https://docs.auxilia.app/errors/AUX-…` e `traceId` su ogni errore, anche del framework (404 `AUX-10017`, 405 `10018`, richiesta illeggibile `10019`); `Result` → HTTP (`ToHttpResult`/`ToProblem`); nuovo `ErrorType.PreconditionFailed` → 412 (ADR 0012 aggiornato); `GlobalExceptionHandler` (annullamento client 499 senza errore, timeout `10013` → 503, resto `10001` → 500 senza dettagli); header di sicurezza (nosniff, DENY, CSP `default-src 'none'`, no-referrer, `no-store`, niente `Server`, HSTS fuori da Development); OpenAPI 3.1 + Scalar solo in Development, route pubblicate come `/api/v1/…`; contratto `backend/openapi/v1.json` verificato da test, `api-client` rigenerato e controllato in CI. Estensione `IApiEndpoints` (sostituita dai descrittori di modulo in P1-11). 348 test. Rimandati: `DbUpdateConcurrencyException` → `AUX-10010` (con EF, P1-05/P1-08), 401/403 di autenticazione con codici (P2) |
| 2026-09-29 | P1-05 | Completato | `IOperationRunner` (`OperationRunner`): activity `Auxilia` col nome dell'operazione, scope di log (Operation, ActorType, UserId, contesto, entità da `SetEntity`), transazione solo per le scritture (`IOperationTransactionFactory`, default no-op fino a P1-08), metriche `auxilia.operation.duration/count{operation,outcome}`, **un** log di esito (Information codice di successo / Warning codice dell'errore / Error `10001` o `10013`), eccezioni note via `IExceptionClassifier`, eccezioni inattese marcate (`ExceptionLogging`) così il gestore globale non le registra due volte; annullamento senza log. Catalogo `Operations.*` (`OperationDescriptor`) + test (nomi, unicità, codice nel range) e colonna nel registro. `ICurrentUser` (`ActorType` Anonymous/User/Platform/System; `HttpCurrentUser` nell'Api, `SystemCurrentUser` di default). Validazione FluentValidation → `AUX-10020` con campi camelCase e chiavi di traduzione. Paginazione: `PageRequest` + validatore (max 100), `PagedResponse<T>` nei Contracts, `SortMap<T>` con whitelist e tiebreaker. `AddApplication()` in Api e Worker. **Gate di coverage** con `coverlet.MTP` (ADR 0014, sostituisce `coverlet.collector`): SharedKernel+Domain 90%, Application 94,5%. 375 test. Rimandati: `ToPagedResultAsync` con EF Core e test su Postgres (P1-08), classificatore `DbUpdateConcurrencyException` → `AUX-10010` (P1-08) |
| 2026-09-29 | P1-06 | Completato | `Persistence.Catalog` (EF Core 10 + Npgsql, snake_case, schema `catalog`, estensione `citext`): tenants (slug citext univoco, stato come stringa, connection string cifrata, versioni, lingua, fuso), tenant_domains, modules, plans + plan_modules (ruoli `text[]`), tenant_plans (vincolo di validità), tenant_module_overrides, platform_users + platform_user_roles, platform_settings (jsonb), client_applications, migration_runs, data_protection_keys; colonne di audit ombra (`CatalogAuditInterceptor`, attore `platform:{id}`/`system`), `xmin` come token di concorrenza; seed piano `standard` (id fisso). Dominio in `Domain/Platform` (`Tenant` con macchina a stati, `Plan`, `ClientApplication`, `MigrationRun`…); regola dello slug unica in `SharedKernel.Tenancy.TenantSlug` (3–40 caratteri, usata anche dai file di log); codici `AUX-11005…11007`. Data Protection con chiavi nel Catalog (`AddCatalogPersistence`). Migrazione `Catalog_Initial` (`dotnet-ef` come tool locale, factory di design in MigrationRunner; migrazioni marcate `generated_code`). `Auxilia.Persistence.Tests` con Testcontainers Postgres 18: migrazione da zero, nessuna modifica del modello mancante, seed, audit, slug univoco case-insensitive, concorrenza `xmin`, array di ruoli, vincolo di validità, chiavi DP condivise. 416 test. Rimandati: protezione a riposo delle chiavi DP con certificato (H-03), registrazione del Catalog in Api/Worker (P1-07) |
| 2026-09-29 | P1-07 | Completato | Tenancy: `ITenantContext`/`ITenantContextSetter` (scoped, un solo tenant per scope), `ITenantDirectory` sul Catalog con cache in memoria di 30 s (host sconosciuti inclusi), `ITenantConnectionProtector` (Data Protection, purpose `Auxilia.Tenancy.ConnectionString.v1`), `ITenantDbContext` + `ITenantDbContextFactory` (Application su EF Core base) con `TenantDbContext` (modello vuoto fino a P1-08) e un `NpgsqlDataSource` per tenant. `TenantResolutionMiddleware`: claim `tenant` → header `X-Tenant` → host (`{slug}.{BaseDomains}` o dominio custom); claim diverso → 403 `AUX-11004` con log; tenant sconosciuto → 404 `AUX-11009`; `TenantSlug` nello scope di log (file del tenant) e nella traccia. `RequireTenant()` per gli endpoint del tenant: nessun tenant 400 `11008`, sospeso 423 `11011`, provisioning/migrazione fallita 503 `11010`, archiviato 404 (gli endpoint di piattaforma non usano il filtro). Api: Catalog obbligatorio (`ConnectionStrings:Catalog` da user-secrets/ambiente). Test di integrazione su Postgres condiviso (assembly fixture, 5 tenant in stati diversi, 2 database): header, maiuscole, sottodominio, dominio custom, claim, cross-tenant, sconosciuto/`../etc`, stati, isolamento dei database. 433 test. Rimandati: invalidazione immediata della cache tenant (P1-10), pipeline tenant nel Worker (P1-12), eventi di sicurezza `29xxx` per il cross-tenant (con `Log.Security`, P2) |
| 2026-09-29 | P1-08 | Completato | `Persistence.Tenant` base: `TenantSchemas` (uno schema per modulo), convenzioni (`IAuditable` → `created_*/updated_*` uuid; aggregati → `xmin`, eventi di dominio non mappati; `ISoftDeletable` → `is_deleted/deleted_at/deleted_by` + filtro globale), `TenantAuditInterceptor` (colonne di audit, delete → soft delete, `audit.entity_changes` con azione, modifiche old/new in jsonb, `actor_type`, attore, trace id, stessa transazione); tabelle `ops.data_migrations_history`, `ops.job_runs`, `ops.number_sequences`, `ops.legacy_id_map`, storico migrazioni EF in `ops`; migrazione `Tenant_Initial`. `IDataMigration` (`D_YYYYMMDD_NNN`, `[IncludedInInitialSeed(false)]`) + `DataMigrationRunner` (ordine per chiave, una transazione per data-migration, storico con durata, `AUX-28001/28002`, `MarkCoveredBySeedAsync`, versione dati) + `TenantInitialSeed`. **Transazione reale dell'`IOperationRunner`** (`TenantOperationTransactions`: connessione aperta dal primo contesto, condivisa da tutti i contesti dell'operazione, commit una volta, rollback su errore, operazioni annidate unite). `EfCoreExceptionClassifier`: `DbUpdateConcurrencyException` → 409 `AUX-10010`. Test su Postgres: tabelle, modello allineato, data-migration due volte/errore/seed, transazioni (commit, rollback, annidate, senza operazione), audit/soft delete/xmin su aggregato di esempio; test di architettura sulle chiavi. 456 test. Rimandati: outbox e messaggi processati (con Rebus, P1-12), allocatore di numeri su `number_sequences` (con la prima entità che lo usa), esclusione di campi sensibili dallo storico (con le prime entità con dati sensibili) |
| 2026-09-29 | P1-09 | Completato | `auxctl` (MigrationRunner): `migrate catalog`, `migrate tenants --tenant/--all` (schema + data-migrations, un tenant che fallisce non ferma gli altri), `tenant provision/suspend/reactivate/archive/list`, `jobs list/run --tenant/--all`, `diagnostics registry`; parser scritto in casa (niente pacchetto CLI); codici di uscita 0/1/2; log sugli stessi file giornalieri (Runner → ServiceDefaults). Logica nei Manager dell'Application (`ITenantLifecycleManager`, `ITenantMigrationManager`, registrati solo con `AddTenantAdministration()`; `IJobRunner` + `IRecurringJob`) con porte `ICatalogStore`/`ICatalogMigrator` (Catalog), `ITenantDatabaseAdmin` (ruolo e database `auxilia_t_<slug>` con password generata, CONNECT revocato a PUBLIC, `--existing-database` da variabile d'ambiente), `IJobRunStore` (`ops.job_runs`). Provisioning riprendibile (Provisioning/MigrationFailed → ripresa, database riusato), slug riservati, `migration_runs` per ogni esecuzione. Namespace `Application.Operations` rinominato `Application.Execution` (conflitto con il catalogo `Operations`). Codici `AUX-11012…11018`, `28003…28005`, `26001…26003`. Runbook `docs/runbooks/tenant-provisioning.md`. Test end-to-end di auxctl su Postgres (ciclo di vita completo, isolamento dei ruoli, ripresa, database fornito dal DBA, errori) + unit test dei Manager. 481 test. Rimandati: primo amministratore e link di attivazione (P2), lock distribuito dei job (Redis, P1-10), provisioning dalla console via Worker (N02) |
| 2026-09-29 | P1-10 | Completato | Impostazioni: `SettingDefinition<T>` (chiave `modulo.nome` camelCase, modulo, scope Platform/Tenant/User, default validato, regola, chiave di descrizione, JSON con enum come stringhe) e `SecretSettingDefinition` (mai utente); registry con chiavi univoche (`AddSettingDefinitions`); definizioni legacy di §7.2 nei moduli (`registration.*`, `cases.expiry.*`, `auth.session.idleMinutes`, `auth.singleSession`, `documents.storage.provider`, `documents.maxUploadMb`). `ISettingsProvider`: utente → tenant → piattaforma → default, valore salvato non valido ignorato con `AUX-20004`; segreti cifrati con Data Protection e decifrati solo in `GetSecretAsync`. `ISettingsManager` (`Configuration.SetSetting/ResetSetting`, `AUX-20001…20006`) su porte `IPlatformSettingStore` (Catalog) e `ITenantSettingStore` (tabelle `configuration.settings` e `configuration.user_settings`, migrazione `Configuration_AddSettings`, scritture auditate). Cache: `IReferenceDataCache` su HybridCache + `ReferenceDataCache<T>` (snapshot per tenant `t:{slug}:configuration:snapshot:current`, tag tenant + piattaforma, L1 60 s / L2 30 min); Redis/Valkey opzionale (`ConnectionStrings:Redis`) dietro circuit breaker (`AUX-24001/24002`), `PUBLISH auxilia:invalidate` per svuotare la L1 degli altri nodi (`24003/24004`), health check `redis` Degraded. `IOperationScope.OnCommitted`: azioni eseguite dopo il commit dell'operazione più esterna (`AUX-10021` se falliscono). Risoluzione tenant sulla stessa cache con invalidazione immediata dal ciclo di vita (rimandata da P1-07). Worker con Catalog + persistence tenant. Nuovo progetto `Auxilia.Infrastructure.Tests` (Valkey: L2 condivisa, invalidazione tra nodi, Redis giù); test Postgres degli store e della pipeline; test API con Redis irraggiungibile. 544 test; coverage Domain 84%, Application 98%. Ambiente cloud: SDK estratto dall'immagine MCR (download .NET bloccato), immagini Docker dal mirror GCR. Rimandati: endpoint/editor delle impostazioni e branding (S-02), autorizzazione sul Manager (P2-03/P2-06), FK di `user_settings` verso `identity.users` (P2-01), lock distribuito dei job (P1-12), backplane SignalR sulla stessa connessione Redis (P2-05) |
| 2026-09-30 | P1-11 | Completato | `IModuleDescriptor` (codice, Core/Optional, range codici evento, permessi, impostazioni, navigazione per ruolo, `AddServices`) in Application senza HTTP; `IModuleEndpoints` nell'Api con gruppo filtrato (tenant attivo → modulo visibile, altrimenti 404 `AUX-10017` identico a una route inesistente). 11 descrittori (Core: identity, configuration, localization, messaging; Optional: directory, cases, scheduling, documents, engagement, marketing, reporting) con le impostazioni di P1-10 e menu per ruolo dal legacy (F22); registry con codici e range univoci; test di architettura su registrazione, range, namespace, navigazione. Visibilità (§5.2): Core per tutti, altrimenti override ?? piano valido ora, per ruolo; `IModuleAccess` in cache (`t:{slug}:platform:modules:current`); ruoli da tutte le claim `role` (`ICurrentUser.Roles`, Q39). `INavigationQueryService` (unione dei ruoli, ordinato) pronto per `/me/navigation`. `IModuleCatalogManager.SyncAsync` (`Tenancy.SyncModules`, `AUX-11019`) eseguita da `auxctl migrate catalog`: moduli nuovi nel piano `standard` per tutti i ruoli, moduli rimossi non disponibili, scelte del System preservate, invalidazione dopo il commit. Test: unit (registry, visibilità, cache, navigazione, sync), Postgres (sync idempotente, reader con piano valido e override), API (404 per ruolo/override/anonimo, Core per tutti, stato tenant prima del modulo, override dopo invalidazione). 574 test; coverage Domain 84%, Application 98%. Rimandati: permessi di ruolo e `/me/navigation` (P2-03), modifica piani/override dal System con invalidazione (S-01), descrittore `imports` (S-08) |
| 2026-09-30 | P1-12 | Completato | Rebus 8 + RabbitMQ (pacchetti in allowlist, MIT; RabbitMQ.Client Apache-2.0): routing per convenzione `Messages.V<n>.<Modulo>` → `auxilia.<modulo>`, `ITenantMessage`, bus solo invio per Api/auxctl, Worker con un bus per coda gestita (`MessageRouting.QueuesHandledBy`). Header (`rbs2-msg-id`, tenant, correlazione, utente, attore, `traceparent`) catturati alla produzione. Outbox transazionale `ops.outbox_messages` inviato dopo il commit con `OnCommitted` (nessun polling, D-15; pendenti → job manuale `bus.outbox`, `AUX-23008`). `IIncomingMessageProcessor` (operazione `Bus.HandleMessage`, `AUX-23001`): tenant da header (mancante/non attivo → `error` senza retry, `23003/23004`), chiamante e correlazione dagli header, traccia unita, idempotenza `ops.processed_messages` nella stessa transazione (handler transazionali) o dopo il successo (job). Retry: 5 immediati, secondo livello 10 s/1 min/5 min/30 min con deferral in `rebus_timeouts` del Catalog (`23006`), poi `error` (`23007`); `Result` fallito = permanente (`MessageRejectedException`, `23005`). `RunRecurringJobCommand` → `RunRecurringJobHandler` → `IJobRunner`; lock per tenant/job con advisory lock PostgreSQL (`IJobLock`, `AUX-26004`, niente Redis necessario); `ops.job_runs` fuori dalla transazione. Migrazione `Ops_AddOutboxAndProcessedMessages`. Test: unit (outbox, dispatcher, job, header, processore, lock), end-to-end con RabbitMQ + Postgres in `Worker.IntegrationTests` (outbox → Worker → job una volta con attore del mittente, consegna duplicata, errore transitorio con retry di secondo livello, rifiuto e tenant mancante in `error`, retry esauriti); corretto un test di P1-10 con race sulla scrittura L2 in background. 599 test; coverage Domain 84%, Application 98,7%. Rimandati: health check RabbitMQ (H-03), outbox del Catalog per i messaggi di piattaforma (con S-01), code `auxilia.*` delle feature (con i rispettivi task), comando `auxctl`/console per accodare i job (S-01) |
| 2026-09-30 | P1-13 | Completato | Modulo Messaging (N03, D-16): dominio `MessagingAccount` (default unico per canale e attivo, `AUX-25017`), `SenderRule` (ruolo come stringa per non dipendere dal modulo Platform), `MessageTemplate` (sistema/personalizzato), `OutboundMessage` (Queued → Sent/Failed, tentativi, codice errore); tabelle `configuration.messaging_accounts/sender_rules`, `messaging.message_templates/outbound_messages` (migrazione `Messaging_Initial`). `IMessagingAccountManager`: account con segreto protetto (Data Protection, purpose dedicato), primo account = default, regole per canale sostituite e validate, invio di prova sincrono registrato; snapshot account/regole in cache senza segreti, invalidato dopo il commit. Risoluzione (canale, scopo, ruolo) → (canale, scopo, qualsiasi) → default, solo account attivi, priorità minore vince. `IMessageDispatcher` (API pubblica del modulo): template Liquid (Fluid, HTML-encoded nel corpo) nella lingua del destinatario → tenant → en, `OutboundMessage` Queued + `DeliverOutboundMessageCommand` nell'outbox. Worker: `DeliverOutboundMessageHandler` (coda `auxilia.messaging`), permanente → Failed, transitorio → tentativo + retry. Adapter `smtp` con MailKit (None/StartTls/SslOnConnect, 5xx e credenziali = permanenti); WhatsApp solo nel modello (`AUX-25013`). Data-migration `D_20260930_001` con 6 template di sistema EN+IT, idempotente e rispettosa delle personalizzazioni. Codici `AUX-25001…25021`. Pacchetti MailKit/MimeKit/BouncyCastle, Fluid.Core/Parlot/TimeZoneConverter (MIT, in allowlist). Test: unit (manager, dispatcher, risoluzione, consegna, dominio), adapter SMTP contro un server SMTP in-process (`Tests.Common/FakeSmtpServer`), Fluid, Postgres (seed idempotente, default unico), end-to-end account → dispatcher → outbox → Worker → SMTP → Sent. 638 test; coverage Domain 85,9%, Application 98,4%. **Fase 1 conclusa.** Rimandati: pagine/endpoint console account, regole, registro invii e template (S-03), autorizzazione System (P2-06), template campagne (M-03), adapter WhatsApp (quando servirà, D-20), stato Failed per i messaggi finiti in `error` dopo i retry transitori |
| 2026-09-30 | P2-01 | Completato | Schema `identity` (`users` con `user_name` citext univoco, `roles` di riferimento, `user_roles`) e `directory.people` minimo (migrazione `Identity_AddUsersAndPeople`, un account per persona). `TenantRole` spostato in `SharedKernel.Tenancy` (usato da Identity e Platform senza violare i confini dei moduli). Aggregato `User`: accesso `is_active` indipendente dallo stato cliente (D-05), nessuna password fino all'attivazione (D-06), formato password Identity/LegacyBcrypt, security stamp aggiornato con password, ruoli e disattivazione, lockout progressivo (durata raddoppiata a ogni blocco, massimo 24 h). Hasher composito: `PasswordHasher` di ASP.NET Identity (`Microsoft.Extensions.Identity.Core`, PBKDF2) + BCrypt.Net-Next per gli hash legacy, rehash al primo login (F01). Non si usano store EF/UserManager di Identity (non adatti al database per tenant). `IUserAccountManager` (creazione su persona esistente, password con lunghezza minima, attivazione, ruoli) e `IPasswordAuthenticator` (username case-insensitive, errore generico `AUX-12002` senza enumerazione e con tempo uniforme, lockout `AUX-12003`, motivo solo nel log di sicurezza). Impostazioni `auth.password.minLength` (12), `auth.lockout.maxFailedAttempts` (5), `auth.lockout.minutes` (5). Codici `AUX-12001…12012`, eventi di sicurezza `AUX-29001…29006`. Test: dominio, servizi con fake, hasher (Identity e BCrypt reale), Postgres (login legacy case-insensitive con rehash, unicità case-insensitive, un account per persona). 663 test; coverage Domain 86,7%, Application 98,5%. Rimandati: token/refresh/logout e attivazione/reset via email (P2-02), permessi (P2-03), policy completa, storico e scadenza password, tentativi in `identity.login_attempts` (P2-08), `auxctl users` (P2-07) |
| 2026-09-30 | P2-02 | Completato | Token (F01, F17): access token JWT ES256 (`Microsoft.IdentityModel.JsonWebTokens`, MIT) con `sub`, `tenant`, `sid`, `role`, `client_id`, `jti`, durata `auth.accessToken.minutes` (10); chiavi di firma in `catalog.signing_keys` (migrazione `Catalog_AddSigningKeys`, privata cifrata con Data Protection, una sola attiva), ring in memoria per nodo (refresh 5 min o `kid` sconosciuto, con limite anti-flood), prima chiave creata al primo token, `GET /.well-known/jwks.json`, rotazione manuale `auxctl keys rotate` (vecchia chiave valida 2 h, D-15). API: JwtBearer (`Microsoft.AspNetCore.Authentication.JwtBearer`, MIT) solo ES256, issuer/audience da `Auth`, claim non rimappate, `UseAuthentication` prima della risoluzione tenant (claim autorevole) e 401 ProblemDetails `AUX-10022` (403 `AUX-10023`). Sessioni in `identity.refresh_sessions` + `refresh_tokens` (solo hash SHA-256) + `user_tokens` (migrazione `Identity_AddSessionsAndTokens`): refresh rotante, riuso → sessione revocata (`AUX-29007`), idle scorrevole `auth.session.idleMinutes` + assoluta `auth.session.absoluteDays` (14), security stamp cambiato → sessione chiusa, sessione singola D-08. Deny-list su `IDistributedCache` (`t:{slug}:auth:deny:{jti}` / `deny-sid:{sid}`, Redis se configurato, fail-open) controllata in `OnTokenValidated` (`AUX-12023`). Client app (D-03): `X-Client-Id`/`X-Client-Secret`, segreto confidenziale solo come hash, `auxctl clients add/list` (`AUX-12026/12027`). Endpoint `/api/v1/auth/token` (password, refresh_token), `logout`, `activate`, `password/forgot` (sempre 202, nessuna enumerazione), `password/reset` (chiude tutte le sessioni); link monouso via `IMessageDispatcher` (template `account-activation`, `password-reset`; `auth.activation.linkHours` 72, `auth.passwordReset.linkMinutes` 60, `auth.appBaseUrl`). `MessageTemplates` spostato in `Messaging.Public` (confini dei moduli). L'interceptor di audit del Catalog riconosce le entità auditate da `CreatedBy` (non da un `CreatedAt` di dominio). Contratto OpenAPI e client TS rigenerati. Test: dominio (sessioni, token, chiavi), unit (SessionManager, AccountLinkManager, client, rotazione), infrastruttura (firma/validazione, JWKS senza parte privata, rotazione e kid sconosciuto, deny-list con TTL), API end-to-end (login → endpoint protetto, token di altro tenant 403, client errato, rotazione e riuso, logout, reset e attivazione, JWKS), auxctl (`keys rotate`, `clients`). 709 test; coverage Domain 85,6% (SharedKernel 91,7%), Application 98,8%. Rimandati: `ForceLogout` push (P2-05), pagina/API sessioni attive (B-20/B-21), rate limiting e captcha su `/auth/*` (P2-04), token di piattaforma e 2FA (P2-06), invio attivazione dalla creazione cliente (B-01), OTP e storico password (P2-08), binding dispositivo mobile |
