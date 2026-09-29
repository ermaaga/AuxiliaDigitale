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
- Fase: **Fase 0 — in corso** (P0-01, P0-02 completati)
- Prossimo task: **P0-03** (workspace frontend)
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
| [ ] P0-03 | Workspace frontend pnpm: `apps/web`, `packages/{ui,api-client,config}`, Tailwind v4, shadcn/ui. | 1 | P0-02 | – | frontend-feature, ui-design |
| [ ] P0-04 | CI: build+test, NuGetAudit, licenze, `pnpm audit`, OSV-Scanner, gitleaks, Dependabot. | 1 | P0-03 | F32 | dependency-policy |
| [ ] P0-05 | `Architecture.Tests`: layer, confini tra moduli, blocklist, naming. | 0,5 | P0-02 | – | testing |

### Fase 1 — Fondamenta backend (≈ 15 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] P1-01 | `SharedKernel`: Result, Error, Entity/AggregateRoot, eventi, guard, Guid v7 + test. | 1 | P0-05 | – | backend-feature |
| [ ] P1-02 | `Diagnostics`: EventCodes (19000 Marketing, 25000 Messaging), `Log.*`, `Errors.*`, registro generato, test unicità/range. | 1 | P1-01 | F25 | log-codes |
| [ ] P1-03 | `ServiceDefaults` + `AppHost`; Serilog: console JSON + **file giornalieri per tenant** (`logs/tenants/{slug}/…`, `logs/platform/…`) su storage `local-file`/`azure-blob`, Information+, mascheramento, **livello minimo per tenant a tempo** (D-28); OTel predisposto; health. | 1,5 | P1-02 | F25, F32 | log-codes, observability |
| [ ] P1-04 | Host API: versioning, ProblemDetails+`errorCode`, `IExceptionHandler`, OpenAPI+Scalar, export OpenAPI in CI, header sicurezza. | 1 | P1-03 | F28, F29 | api-contract |
| [ ] P1-05 | Base applicativa (D-26): convenzioni Manager/QueryService/Api pubblica, `IOperationRunner` (traccia, scope di log, transazione+outbox, metriche, log di esito con codice, mappatura eccezioni), catalogo `Operations.*`, `ICurrentUser`, validazione, contratto di paginazione, registrazione DI + test. | 1 | P1-04 | F28 | backend-feature, log-codes |
| [ ] P1-06 | `Persistence.Catalog`: tenants, domini, **modules, plans, plan_modules, tenant_plans, tenant_module_overrides**, platform_users, platform_settings, client_applications, migration_runs, chiavi DP; seed piano `standard`. | 1,5 | P1-05 | F22, N02 | multitenancy, ef-migration |
| [ ] P1-07 | Tenancy: `ITenantContext`, risoluzione (claim/header/host), 403 cross-tenant, `ITenantDbContextFactory`, test isolamento. | 1 | P1-06 | – | multitenancy |
| [ ] P1-08 | `Persistence.Tenant` base: schemi, audit interceptor (`actor_type`), `xmin`, soft delete, `ops.*` (incl. `job_runs`), `IDataMigration` runner, Persistence.Tests. | 1,5 | P1-07 | F29, F30 | ef-migration, data-migration |
| [ ] P1-09 | `auxctl`: migrate catalog/tenants, tenant provision/suspend/archive, diagnostics registry, `jobs run`. | 1,5 | P1-08, D-02 | F30, F32, N02 | tenant-provisioning |
| [ ] P1-10 | Impostazioni: `SettingDefinition<T>`, 5 livelli, `ISettingsProvider`, `ReferenceDataCache<T>` (HybridCache + Redis + PUBLISH invalidazione L1), segreti cifrati. | 1 | P1-08 | F23 | caching |
| [ ] P1-11 | Moduli: `IModuleDescriptor`, registry, sincronizzazione `catalog.modules`, calcolo moduli effettivi (piano ∩ override ∩ ruolo) in cache, filtro endpoint 404, builder navigazione. | 1 | P1-06, P1-10 | F22, N02 | architecture |
| [ ] P1-12 | Rebus + RabbitMQ: code, outbox, handler idempotenti, retry, `error`; Worker (solo code); registry `IRecurringJob` + esecuzione manuale con lock e `ops.job_runs` (nessuno scheduler). | 1,5 | P1-08 | F11 | messaging-rebus |
| [ ] P1-13 | `Messaging`: `IMessageChannel`, `messaging_accounts`, `sender_rules` (scopo × ruolo), risoluzione account, adapter `smtp` (MailKit), template Liquid EN/IT, `outbound_messages`, invio di prova; canale WhatsApp solo nel modello. | 1,5 | P1-12, P1-10 | F23, N03 | messaging-rebus, localization |

### Fase 2 — Identità e sicurezza (≈ 9 gg)
| ID | Task | Stima | Dip. | F/N | Skill |
|---|---|---|---|---|---|
| [ ] P2-01 | Schema `identity` (+ `directory.people` minimo), Identity, hasher BCrypt legacy → rehash, lockout, policy; accesso ≠ stato cliente (D-05); ruoli Administrator/Employee/Client. | 1 | P1-09 | F01, F31 | security |
| [ ] P2-02 | JWT ES256 + JWKS, client app, refresh rotante con riuso, logout, deny-list; attivazione account e reset password via email (D-06). | 1,5 | P2-01, P1-13, D-03 | F01, F17 | security |
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
